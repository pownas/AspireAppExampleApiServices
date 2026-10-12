using System.Diagnostics;
using Microsoft.Extensions.Configuration;
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
    public void ConfigureOpenTelemetry_PreservesSampledParentForBlazorChild()
    {
        var sampledParent = new ActivityContext(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded,
            isRemote: true);

        var isRecorded = IsRecorded(new Dictionary<string, string?>
        {
            ["OTEL_TRACES_SAMPLER"] = "parentbased_always_off"
        }, sampledParent, "Blazor.Child");

        Assert.IsTrue(isRecorded);
    }

    [TestMethod]
    public void ConfigureOpenTelemetry_WebHostDoesNotSubscribeToSignalRHubActivities()
    {
        var isRecorded = TryRecordActivity(
            new Dictionary<string, string?>
            {
                ["OTEL_TRACES_SAMPLER"] = "always_on"
            },
            "Microsoft.AspNetCore.Components.Server.ComponentHub/StartCircuit",
            "AspireApp1.Web",
            "Microsoft.AspNetCore.SignalR.Server");

        Assert.IsNull(isRecorded, "The Blazor web host should not listen to SignalR hub activities.");
    }

    private static bool IsRecorded(
        IDictionary<string, string?> configuration,
        ActivityContext parentContext = default,
        string activityName = "sampling-test")
    {
        var isRecorded = TryRecordActivity(configuration, activityName, ActivitySourceName, ActivitySourceName, parentContext);
        Assert.IsNotNull(isRecorded, "The test ActivitySource should be subscribed by the tracer provider.");
        return isRecorded.Value;
    }

    private static bool? TryRecordActivity(
        IDictionary<string, string?> configuration,
        string activityName,
        string applicationName,
        string activitySourceName,
        ActivityContext parentContext = default)
    {
        var builder = new HostApplicationBuilder();
        builder.Environment.ApplicationName = applicationName;
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.ConfigureOpenTelemetry();
        using var services = builder.Services.BuildServiceProvider();
        using var tracerProvider = services.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource(activitySourceName);
        using var activity = source.StartActivity(activityName, ActivityKind.Internal, parentContext);

        return activity?.Recorded;
    }
}
