using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AspireApp1.StateStore;
using AspireApp1.WorkerService1;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<WorkerJobQueue>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<PeriodicChainTrigger>();
builder.Services.AddHttpClient("apiservicestaticweather", client =>
{
    client.BaseAddress = new Uri("https+http://apiservicestaticweather");
});
builder.Services.AddHttpClient("workerservice2", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice2");
});
builder.Services.AddHttpClient("workerservice3", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice3");
});

builder.Services.AddDbContext<StateStoreDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("statestore")
        ?? $"Data Source={Path.Combine(Path.GetTempPath(), "AspireApp1StateStore", "statestore.db")}"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseTraceContextLogScope();

// Ensure the database schema exists (idempotent — also adds new tables to existing DBs)
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
    await DatabaseInitializer.EnsureSchemaAsync(db);
}

var flowActivitySource = new ActivitySource("AspireApp1.WorkerService1.Flow");

app.MapPost("/jobs", async (WorkerJobMessage message, WorkerJobQueue queue, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(message.TraceParent))
    {
        logger.LogWarning("Worker job missing traceparent. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc}",
            System.Diagnostics.Activity.Current?.TraceId.ToString(),
            System.Diagnostics.Activity.Current?.SpanId.ToString(),
            System.Diagnostics.Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow);
        return Results.BadRequest("traceparent is required.");
    }

    var queuedMessage = message with { CorrelationId = ResolveCorrelationId(message, httpContext, logger, hostEnvironment) };

    await queue.EnqueueAsync(queuedMessage, httpContext.RequestAborted);

    logger.LogInformation("Worker job queued {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
        message.JobId,
        System.Diagnostics.Activity.Current?.TraceId.ToString(),
        System.Diagnostics.Activity.Current?.SpanId.ToString(),
        System.Diagnostics.Activity.Current?.ParentSpanId.ToString(),
        hostEnvironment.ApplicationName,
        DateTimeOffset.UtcNow,
        queuedMessage.CorrelationId);

    return Results.Accepted($"/jobs/{message.JobId}");
});

// ── Flow trigger endpoint ──────────────────────────────────────────────────
// POST /flow/start
// Starts a named multi-step flow: Step1 runs synchronously here (WS1),
// then Step2 and Step3 are forwarded asynchronously to WS2 → WS3.
app.MapPost("/flow/start", async (
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    ILogger<Program> logger,
    IHostEnvironment hostEnvironment) =>
{
    using var rootActivity = flowActivitySource.StartActivity("FlowRun.Start", ActivityKind.Server);
    var flowRunId = Guid.NewGuid().ToString("N");
    var correlationId = Guid.NewGuid().ToString("N");
    var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString();

    rootActivity?.SetTag("flow.run.id", flowRunId);
    rootActivity?.SetTag("correlation.id", correlationId);

    logger.LogInformation("Flow start requested. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
        flowRunId, traceId, correlationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);

    // Persist flow run + all steps (all Pending initially)
    await using (var scope = scopeFactory.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
        db.FlowRunRecords.Add(new FlowRunRecord
        {
            FlowRunId = flowRunId,
            FlowName = "DemoFlow",
            CorrelationId = correlationId,
            TraceId = traceId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = FlowRunStatus.Running
        });
        db.FlowStepRecords.Add(new FlowStepRecord
        {
            FlowRunId = flowRunId,
            StepName = "Step1.SyncValidate",
            ServiceName = hostEnvironment.ApplicationName,
            StepOrder = 1,
            Status = FlowStepStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });
        db.FlowStepRecords.Add(new FlowStepRecord
        {
            FlowRunId = flowRunId,
            StepName = "Step2.AsyncProcess",
            ServiceName = "AspireApp1.WorkerService2",
            StepOrder = 2,
            Status = FlowStepStatus.Pending
        });
        db.FlowStepRecords.Add(new FlowStepRecord
        {
            FlowRunId = flowRunId,
            StepName = "Step3.AsyncFinalize",
            ServiceName = "AspireApp1.WorkerService3",
            StepOrder = 3,
            Status = FlowStepStatus.Pending
        });
        await db.SaveChangesAsync();
    }

    // ── Step 1: synchronous work in WS1 ──────────────────────────────────
    string? step1SpanId = null;
    string? step1Error = null;
    using (var step1Activity = flowActivitySource.StartActivity("FlowStep1.SyncValidate", ActivityKind.Internal))
    {
        step1SpanId = step1Activity?.SpanId.ToString();
        try
        {
            var weatherClient = httpClientFactory.CreateClient("apiservicestaticweather");
            var response = await weatherClient.GetAsync("/infoweather");
            if (!response.IsSuccessStatusCode)
            {
                step1Activity?.SetStatus(ActivityStatusCode.Error, $"Status: {response.StatusCode}");
                step1Error = $"HTTP {(int)response.StatusCode} from /infoweather";
            }

            logger.LogInformation("Flow step 1 completed. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
                flowRunId, traceId, correlationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            step1Activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            step1Error = ex.Message;
            logger.LogError(ex, "Flow step 1 failed. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id}", flowRunId, traceId, correlationId);
        }
    }

    // Update Step1 record
    await using (var scope = scopeFactory.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
        var step1 = await db.FlowStepRecords
            .FirstOrDefaultAsync(s => s.FlowRunId == flowRunId && s.StepName == "Step1.SyncValidate");
        if (step1 is not null)
        {
            step1.Status = step1Error is null ? FlowStepStatus.Completed : FlowStepStatus.Failed;
            step1.CompletedAt = DateTimeOffset.UtcNow;
            step1.ErrorMessage = step1Error;
            step1.TraceId = traceId;
            step1.SpanId = step1SpanId;
            await db.SaveChangesAsync();
        }
    }

    // ── Forward Step2 to WorkerService2 (fire-and-forget) ────────────────
    var stepMessage = new FlowStepMessage(
        FlowRunId: flowRunId,
        StepName: "Step2.AsyncProcess",
        TraceParent: System.Diagnostics.Activity.Current?.Id ?? string.Empty,
        TraceState: System.Diagnostics.Activity.Current?.TraceStateString,
        CorrelationId: correlationId);

    try
    {
        var ws2Client = httpClientFactory.CreateClient("workerservice2");
        var json = JsonSerializer.Serialize(stepMessage);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await ws2Client.PostAsync("/flow/step", content);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to forward flow step to workerservice2. status_code={status_code} flow_run_id={flow_run_id}", response.StatusCode, flowRunId);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception forwarding flow step to workerservice2. flow_run_id={flow_run_id}", flowRunId);
    }

    return Results.Ok(new FlowStartResponse(flowRunId, correlationId, traceId));
});

app.MapDefaultEndpoints();

app.Run();

static string ResolveCorrelationId(WorkerJobMessage message, HttpContext httpContext, ILogger logger, IHostEnvironment hostEnvironment)
{
    if (!string.IsNullOrWhiteSpace(message.CorrelationId))
    {
        return message.CorrelationId;
    }

    var correlationId = httpContext.Items["correlation_id"]?.ToString();
    if (!string.IsNullOrWhiteSpace(correlationId))
    {
        return correlationId;
    }

    var generatedCorrelationId = Guid.NewGuid().ToString("N");
    logger.LogWarning("Generated new correlation_id because upstream correlation context was missing. service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
        hostEnvironment.ApplicationName,
        DateTimeOffset.UtcNow,
        generatedCorrelationId);

    return generatedCorrelationId;
}

internal sealed record FlowStepMessage(
    string FlowRunId,
    string StepName,
    string TraceParent,
    string? TraceState,
    string CorrelationId);

internal sealed record FlowStartResponse(
    string FlowRunId,
    string CorrelationId,
    string? TraceId);
