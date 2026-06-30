using AspireApp1.WorkerService1;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<Worker>();

// Lägg till OpenTelemetry för distribuerad spårning (Digg-standarden)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
               .AddHttpClientInstrumentation()
               .AddSource("AspireApp1.WorkerService1");
    });

var host = builder.Build();
host.Run();
