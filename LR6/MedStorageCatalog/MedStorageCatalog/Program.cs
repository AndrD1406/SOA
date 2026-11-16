using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MedStorage.Catalog.Api.Infrastructure.Database;
using MedStorage.Catalog.Api.Features.RegistrySync;
using MedStorage.Catalog.Api.Infrastructure.Auth;
using MedStorage.Catalog.Api.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);

// Observability: OpenTelemetry traces (Jaeger) + metrics (Prometheus /metrics) + Serilog with TraceId/SpanId
builder.Services.AddMedStorageObservability(builder.Configuration, serviceName: "catalog-api");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithBearer();

// JWT validation + OAuth scope policies (Keycloak)
builder.Services.AddMedStorageJwtAuth(builder.Configuration,
    (CatalogPolicies.Read, "catalog:read"),
    (CatalogPolicies.Write, "catalog:write"));

// Configure CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Configure Database
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Synchronous Communication
builder.Services.AddHttpClient<ExternalDrugRegistryClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ExternalRegistry:BaseUrl"] ?? "http://localhost:5000");
});

// Configure Asynchronous Communication (RabbitMQ)
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h => {
            h.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
        });
        
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseMedStorageObservability();

// Create the database schema on startup (no migrations in this iteration).
// Retries because the database container may still be starting.
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
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
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
