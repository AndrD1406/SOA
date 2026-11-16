using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Inventory.Api.Infrastructure.Database;
using MedStorage.Inventory.Api.Features.CatalogSync;
using MedStorage.Inventory.Api.Features.Kits.Compensation;
using MedStorage.Inventory.Api.Features.A2A;
using MedStorage.Inventory.Api.Features.Kits.UserRegistration;
using MedStorage.Inventory.Api.Features.ServiceAuth;
using MedStorage.Inventory.Api.Infrastructure.Auth;
using MedStorage.Inventory.Api.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

// Observability: OpenTelemetry traces (Jaeger) + metrics (Prometheus /metrics) + Serilog with TraceId/SpanId
builder.Services.AddMedStorageObservability(builder.Configuration, serviceName: "inventory-api");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithBearer();

// JWT validation + OAuth scope policies (Keycloak)
builder.Services.AddMedStorageJwtAuth(builder.Configuration,
    (InventoryPolicies.Read, "inventory:read"),
    (InventoryPolicies.Write, "inventory:write"));

// Service account token (client credentials) for calls to Catalog and the Alerts agent
builder.Services.AddHttpClient<ServiceTokenProvider>();
builder.Services.AddTransient<ServiceTokenHandler>();

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
}).AddHttpMessageHandler<ServiceTokenHandler>();

// A2A (LR4): client used by the Producer agent to discover and delegate tasks to the Alerts agent
builder.Services.AddHttpClient<A2AAgentClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["A2A:AlertsAgentUrl"] ?? "http://alerts.api:8080");
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<ServiceTokenHandler>();

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

    // Event-driven onboarding: UserRegisteredEvent from the User Service -> default kit
    x.AddConsumer<UserRegisteredConsumer>();

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

        cfg.ReceiveEndpoint("inventory-user-registered-queue", e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.UseEntityFrameworkOutbox<InventoryDbContext>(context);
            e.ConfigureConsumer<UserRegisteredConsumer>(context);
        });

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseMedStorageObservability();

// Create the database schema on startup (no migrations in this iteration).
// Retries because the database container may still be starting, and because
// several replicas may race to create the schema at the same time.
await EnsureDatabaseCreatedAsync(app);

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

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
            // EnsureCreated does not alter an existing database: add the kits table introduced with JWT auth
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS kits (
                    "KitId" uuid NOT NULL CONSTRAINT "PK_kits" PRIMARY KEY,
                    "OwnerUserId" uuid NOT NULL,
                    "Name" character varying(100) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL);
                CREATE INDEX IF NOT EXISTS "IX_kits_OwnerUserId" ON kits ("OwnerUserId");
                """);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            app.Logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}), retrying...", attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
