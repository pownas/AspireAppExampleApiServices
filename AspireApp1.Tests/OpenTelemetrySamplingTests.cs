using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace AspireApp1.Tests;

[TestClass]
public class OpenTelemetrySamplingTests
{
    private const string ActivitySourceName = "AspireApp1.Tests.OpenTelemetrySampling";

    [TestMethod]
    public void ConfigureOpenTelemetry_UsesEnvironmentSamplerOverTracingRatio()
    {
        var isRecorded = IsRecorded(new Dictionary<string, string?>
        {
            ["Tracing:SamplingRatio"] = "0",
            ["OTEL_TRACES_SAMPLER"] = "always_on",
            ["OTEL_TRACES_SAMPLER_ARG"] = "0"
        });

        Assert.IsTrue(isRecorded);
    }

    [TestMethod]
    public void ConfigureOpenTelemetry_UsesEnvironmentSamplerArgument()
    {
        var isRecorded = IsRecorded(new Dictionary<string, string?>
        {
            ["Tracing:SamplingRatio"] = "1",
            ["OTEL_TRACES_SAMPLER"] = "parentbased_traceidratio",
            ["OTEL_TRACES_SAMPLER_ARG"] = "0"
        });

        Assert.IsFalse(isRecorded);
    }

    [TestMethod]
    public void ConfigureOpenTelemetry_PreservesSampledBlazorParent()
    {
        var sampledParent = new ActivityContext(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded,
            isRemote: true);

        var isRecorded = IsRecorded(new Dictionary<string, string?>
        {
            ["OTEL_TRACES_SAMPLER"] = "parentbased_always_off"
        }, sampledParent, "Microsoft.AspNetCore.Components.Server.ComponentHub/test");

        Assert.IsTrue(isRecorded);
    }

    private static bool IsRecorded(
        IDictionary<string, string?> configuration,
        ActivityContext parentContext = default,
        string activityName = "sampling-test")
    {
        var environmentVariables = new Dictionary<string, string?>
        {
            ["Tracing__SamplingRatio"] = GetValue(configuration, "Tracing:SamplingRatio"),
            ["OTEL_TRACES_SAMPLER"] = GetValue(configuration, "OTEL_TRACES_SAMPLER"),
            ["OTEL_TRACES_SAMPLER_ARG"] = GetValue(configuration, "OTEL_TRACES_SAMPLER_ARG")
        };
        var previousValues = environmentVariables.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable);

        try
        {
            foreach (var (key, value) in environmentVariables)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            var builder = new HostApplicationBuilder();
            builder.Environment.ApplicationName = ActivitySourceName;
            builder.ConfigureOpenTelemetry();

            using var services = builder.Services.BuildServiceProvider();
            using var tracerProvider = services.GetRequiredService<TracerProvider>();
            using var source = new ActivitySource(ActivitySourceName);
            using var activity = source.StartActivity(activityName, ActivityKind.Internal, parentContext);

            Assert.IsNotNull(activity, "The test ActivitySource should be subscribed by the tracer provider.");
            return activity.Recorded;
        }
        finally
        {
            foreach (var (key, value) in previousValues)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? GetValue(IDictionary<string, string?> configuration, string key) =>
        configuration.TryGetValue(key, out var value) ? value : null;
}
