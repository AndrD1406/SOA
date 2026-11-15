using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Inventory.Api.Infrastructure.Database;
using MedStorage.Inventory.Api.Features.CatalogSync;
using MedStorage.Inventory.Api.Features.Kits.Compensation;
using MedStorage.Inventory.Api.Features.A2A;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Configure Database
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Synchronous Communication (Client for Catalog Service)
builder.Services.AddHttpClient<CatalogServiceClient>(client =>
{
    // The URL should map to the Catalog service in the Docker network
    client.BaseAddress = new Uri(builder.Configuration["CatalogService:BaseUrl"] ?? "http://catalog.api:8080");
});

// A2A (LR4): client used by the Producer agent to discover and delegate tasks to the Alerts agent
builder.Services.AddHttpClient<A2AAgentClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["A2A:AlertsAgentUrl"] ?? "http://alerts.api:8080");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Configure Asynchronous Communication (RabbitMQ)
builder.Services.AddMassTransit(x =>
{
    // Transactional Outbox: IPublishEndpoint inside a request scope writes to the OutboxMessage table
    // and the message is sent only after DbContext.SaveChangesAsync commits.
    x.AddEntityFrameworkOutbox<InventoryDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(1);
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });

    // SAGA compensation: reacts to Fault<MedicineItemAddedEvent> raised by the Alerts Service
    x.AddConsumer<MedicineItemAddedFaultConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h => {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("inventory-medicine-added-fault-queue", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            // Inbox/outbox on the consumer: deduplicates redelivered faults by MessageId
            e.UseEntityFrameworkOutbox<InventoryDbContext>(context);
            e.ConfigureConsumer<MedicineItemAddedFaultConsumer>(context);
        });

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

// Create the database schema on startup (no migrations in this iteration).
// Retries because the database container may still be starting, and because
// several replicas may race to create the schema at the same time.
await EnsureDatabaseCreatedAsync(app);

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();

static async Task EnsureDatabaseCreatedAsync(WebApplication app)
{
    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await db.Database.EnsureCreatedAsync();
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}), retrying...", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
