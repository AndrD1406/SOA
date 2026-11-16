using System.Diagnostics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MedStorage.User.Api.Infrastructure.Observability;

// Observability (LR5): traces -> Jaeger (OTLP), metrics -> Prometheus (/metrics), structured logs (Serilog)
// with TraceId/SpanId on every event, so a log line can be looked up as a trace in Jaeger and vice versa.
// Each microservice keeps its own copy of this file (no shared library between the services).
//
// Configuration (appsettings "Observability" section or env vars Observability__*):
//   OtlpEndpoint  - OTLP gRPC endpoint of the trace collector (Jaeger), e.g. http://jaeger:4317.
//                   Empty => traces are still created (log correlation keeps working) but not exported.
//   LogFormat     - "json" (one compact JSON object per line, for containers) or "text" (default, local dev).
public static class ObservabilityExtensions
{
    // Requests that would only add noise to traces (scrapes, Swagger UI assets)
    private static readonly string[] IgnoredPathPrefixes = ["/metrics", "/swagger", "/health"];

    public static IServiceCollection AddMedStorageObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var section = configuration.GetSection("Observability");
        var otlpEndpoint = section["OtlpEndpoint"];
        var jsonLogs = string.Equals(section["LogFormat"], "json", StringComparison.OrdinalIgnoreCase);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString(),
                    // Distinguishes replicas (e.g. the 2 inventory.api containers): container hostname
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes([
                    new("deployment.environment.name",
                        configuration["ASPNETCORE_ENVIRONMENT"] ?? Environments.Production)
                ]))

            // 1. Distributed tracing
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        o.Filter = ctx => !IgnoredPathPrefixes.Any(p =>
                            ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
                    })
                    .AddHttpClientInstrumentation(o => o.RecordException = true)
                    // MassTransit 8 emits its own spans (publish/send/receive/consume) via ActivitySource "MassTransit"
                    // and propagates the W3C trace context in message headers, so one trace spans HTTP -> RabbitMQ -> consumer.
                    .AddSource("MassTransit");

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            })

            // 2. Metrics, scraped by Prometheus from GET /metrics
            .WithMetrics(metrics => metrics
                // http.server.request.duration histogram -> latency p50/p95/p99, RPS, 5xx error rate
                .AddAspNetCoreInstrumentation()
                // http.client.request.duration -> latency of calls to Catalog / Alerts / Keycloak
                .AddHttpClientInstrumentation()
                // System.Runtime meter: process CPU time, working set, GC, thread pool, exceptions
                .AddRuntimeInstrumentation()
                // MassTransit: messages published/consumed, consume duration, faults
                .AddMeter("MassTransit")
                .AddPrometheusExporter());

        // 3. Structured logging. Replaces the default Microsoft logging providers, so every ILogger<T>
        // (and app.Logger) writes through Serilog.
        services.AddSerilog((sp, logger) =>
        {
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                // IHttpClientFactory logs 4 lines per outgoing call; the spans already carry that information
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                // Optional overrides from the "Serilog" configuration section
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .Enrich.With<ActivityEnricher>()
                .Enrich.WithProperty("Service", serviceName)
                .Enrich.WithProperty("Instance", Environment.MachineName);

            if (jsonLogs)
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            else
                logger.WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {SourceContext} trace={TraceId} span={SpanId}{NewLine}  {Message:lj}{NewLine}{Exception}");
        });

        return services;
    }

    // Exposes /metrics and logs one summary line per HTTP request (method, path, status, elapsed, TraceId).
    // Call right after builder.Build(), before the other middleware.
    public static WebApplication UseMedStorageObservability(this WebApplication app)
    {
        app.UseSerilogRequestLogging(o =>
        {
            // Scrapes every 15s would flood the log
            o.GetLevel = (ctx, _, ex) =>
                ex is not null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : IgnoredPathPrefixes.Any(p => ctx.Request.Path.StartsWithSegments(p)) ? LogEventLevel.Verbose
                : LogEventLevel.Information;
        });

        // Prometheus text format. DisableHttpMetrics: scrapes must not skew the latency/RPS/error-rate series.
        app.MapPrometheusScrapingEndpoint().DisableHttpMetrics();

        return app;
    }

    // Adds TraceId / SpanId of the current Activity (the active OpenTelemetry span) to every log event.
    private sealed class ActivityEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var activity = Activity.Current;
            if (activity is null) return;

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
        }
    }
}
