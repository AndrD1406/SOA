using MassTransit;
using Microsoft.EntityFrameworkCore;
using MedStorage.Alerts.Api.Infrastructure.Database;
using MedStorage.Alerts.Api.Features.Consumers;
using MedStorage.Alerts.Api.Features.Tracking;
using MedStorage.Alerts.Api.Features.A2A;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Configure Database
builder.Services.AddDbContext<AlertsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Expiry tracking rules shared by the RabbitMQ consumer and the A2A agent endpoint
builder.Services.AddScoped<ExpiryTracker>();

// A2A agent (LR4): tasks created through message/send are kept for tasks/get
builder.Services.AddSingleton<A2ATaskStore>();

// Configure Asynchronous Communication (Consumer)
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MedicineItemAddedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("inventory-medicine-added-queue", e =>
        {
            // After 3 failed retries the message goes to "inventory-medicine-added-queue_error" (DLQ)
            // and Fault<MedicineItemAddedEvent> is published for the Inventory Service to compensate
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
            e.ConfigureConsumer<MedicineItemAddedConsumer>(context);
        });
    });
});

var app = builder.Build();

// Create the database schema on startup (no migrations in this iteration).
// Retries because the database container may still be starting.
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
            var db = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
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
