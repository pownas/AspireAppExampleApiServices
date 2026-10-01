using AspireApp1.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<BusinessCorrelationHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddHttpMessageHandler<BusinessCorrelationHandler>();
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var samplingRatio = double.TryParse(builder.Configuration["Tracing:SamplingRatio"],
            NumberStyles.Float, CultureInfo.InvariantCulture, out var configuredRatio)
            && configuredRatio is >= 0 and <= 1
            ? configuredRatio
            : builder.Environment.IsDevelopment() ? 1.0 : 0.1;

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        // Framework request/client access logs are high-volume; domain events retain their own structured logs.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.SetSampler(new DiagnosticNoiseSampler(CreateConfiguredSampler(builder.Configuration, samplingRatio)))
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context => !TraceConventions.IsNoisePath(context.Request.Path.Value)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation(tracing =>
                        tracing.FilterHttpRequestMessage = request =>
                            !TraceConventions.IsNoisePath(request.RequestUri?.AbsolutePath));
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static Sampler CreateConfiguredSampler(IConfiguration configuration, double samplingRatio)
    {
        var samplerName = configuration["OTEL_TRACES_SAMPLER"];
        if (string.IsNullOrWhiteSpace(samplerName))
        {
            return new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio));
        }

        var samplerArgument = ReadSamplerArgument(configuration);
        return samplerName.Trim().ToLowerInvariant() switch
        {
            "always_on" => new AlwaysOnSampler(),
            "always_off" => new AlwaysOffSampler(),
            "traceidratio" => new TraceIdRatioBasedSampler(samplerArgument),
            "parentbased_always_on" => new ParentBasedSampler(new AlwaysOnSampler()),
            "parentbased_always_off" => new ParentBasedSampler(new AlwaysOffSampler()),
            "parentbased_traceidratio" => new ParentBasedSampler(new TraceIdRatioBasedSampler(samplerArgument)),
            _ => new ParentBasedSampler(new AlwaysOnSampler())
        };
    }

    private static double ReadSamplerArgument(IConfiguration configuration)
    {
        var value = configuration["OTEL_TRACES_SAMPLER_ARG"];
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var ratio)
            && !double.IsNaN(ratio)
            && !double.IsInfinity(ratio)
            && ratio is >= 0 and <= 1
                ? ratio
                : 1.0;
    }

    private sealed class DiagnosticNoiseSampler(Sampler parentBased) : Sampler
    {
        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            if (samplingParameters.Name.StartsWith("StatusMonitor.", StringComparison.Ordinal)
                || samplingParameters.Name.StartsWith("Route -> ", StringComparison.Ordinal))
            {
                return new SamplingResult(SamplingDecision.Drop);
            }

            if (samplingParameters.Kind == ActivityKind.Server
                && TryGetRequestPath(samplingParameters.Tags, out var path)
                && TraceConventions.IsNoisePath(path))
            {
                return new SamplingResult(SamplingDecision.Drop);
            }

            return parentBased.ShouldSample(samplingParameters);
        }

        private static bool TryGetRequestPath(IEnumerable<KeyValuePair<string, object?>>? tags, out string path)
        {
            if (tags is null)
            {
                path = string.Empty;
                return false;
            }

            foreach (var (key, value) in tags)
            {
                if (value is not string stringValue)
                {
                    continue;
                }

                if (key is "url.path" or "http.target" or "http.route")
                {
                    path = stringValue;
                    return true;
                }
            }

            path = string.Empty;
            return false;
        }

    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Adding health checks endpoints to applications in non-development environments has security implications.
        // See https://aka.ms/dotnet/aspire/healthchecks for details before enabling these endpoints in non-development environments.
        if (app.Environment.IsDevelopment())
        {
            // All health checks must pass for app to be considered ready to accept traffic after starting
            app.MapHealthChecks(HealthEndpointPath);

            // Only health checks tagged with the "live" tag must pass for app to be considered alive
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    public static IApplicationBuilder UseTraceContextLogScope(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(correlationId) && correlationId.Length <= 128
                && correlationId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                context.Items["correlation_id"] = correlationId;
                context.Response.Headers["X-Correlation-Id"] = correlationId;
            }

            var currentActivity = Activity.Current;
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TraceContext");

            if (TraceConventions.IsNoisePath(context.Request.Path.Value))
            {
                await next();
                return;
            }

            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["trace_id"] = currentActivity?.TraceId.ToString(),
                ["span_id"] = currentActivity?.SpanId.ToString(),
                ["service.name"] = context.RequestServices.GetRequiredService<IHostEnvironment>().ApplicationName,
                ["timestamp_utc"] = DateTimeOffset.UtcNow
            }))
            {
                await next();
            }
        });
    }
}
