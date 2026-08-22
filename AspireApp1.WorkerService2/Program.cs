using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AspireApp1.StateStore;
using AspireApp1.WorkerService2;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<WorkerJobQueue>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient("apiservicestaticweather", client =>
{
    client.BaseAddress = new Uri("https+http://apiservicestaticweather");
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

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
    await DatabaseInitializer.EnsureSchemaAsync(db);
}

var flowActivitySource = new ActivitySource("AspireApp1.WorkerService2.Flow");

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

// ── Flow step endpoint (Step 2) ────────────────────────────────────────────
// Accepts a flow step message, responds 202 immediately, and processes async.
app.MapPost("/flow/step", (
    FlowStepMessage message,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger,
    IHostEnvironment hostEnvironment) =>
{
    // Capture trace context before the HTTP request activity ends
    var capturedTraceParent = Activity.Current?.Id ?? message.TraceParent;
    var capturedTraceState = Activity.Current?.TraceStateString ?? message.TraceState;

    _ = Task.Run(async () =>
    {
        try
        {
            await ExecuteStep2Async(message, capturedTraceParent, capturedTraceState,
                scopeFactory, httpClientFactory, logger, hostEnvironment, flowActivitySource);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Flow step 2 background task failed. flow_run_id={flow_run_id}", message.FlowRunId);
        }
    });

    return Results.Accepted();
});

// ── Retry-demo flow step endpoint (Step 2) ────────────────────────────────
app.MapPost("/flow/retry-demo/step", (
    FlowStepMessage message,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger,
    IHostEnvironment hostEnvironment) =>
{
    var capturedTraceParent = Activity.Current?.Id ?? message.TraceParent;
    var capturedTraceState = Activity.Current?.TraceStateString ?? message.TraceState;

    _ = Task.Run(async () =>
    {
        try
        {
            await ExecuteRetryStep2Async(message, capturedTraceParent, capturedTraceState,
                scopeFactory, httpClientFactory, logger, hostEnvironment, flowActivitySource);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RetryDemo step 2 background task failed. flow_run_id={flow_run_id}", message.FlowRunId);
        }
    });

    return Results.Accepted();
});

app.MapDefaultEndpoints();

app.Run();

static async Task ExecuteStep2Async(
    FlowStepMessage message,
    string? traceParent,
    string? traceState,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    IHostEnvironment hostEnvironment,
    ActivitySource activitySource)
{
    if (!WorkerTraceContext.TryParse(traceParent, traceState, out var parentContext))
    {
        logger.LogWarning("Invalid trace context for flow step 2. flow_run_id={flow_run_id}", message.FlowRunId);
        parentContext = default;
    }

    using var step2Activity = activitySource.StartActivity("FlowStep2.AsyncProcess", ActivityKind.Consumer, parentContext);
    var traceId = Activity.Current?.TraceId.ToString();
    var spanId = step2Activity?.SpanId.ToString();

    // Update Step2 to Running
    await UpdateFlowStepAsync(message.FlowRunId, "Step2.AsyncProcess", FlowStepStatus.Running,
        traceId, spanId, null, null, scopeFactory, logger);

    logger.LogInformation("Flow step 2 started. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
        message.FlowRunId, traceId, message.CorrelationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);

    string? stepError = null;
    try
    {
        // Simulate async processing with a delay
        await Task.Delay(TimeSpan.FromSeconds(3));

        var weatherClient = httpClientFactory.CreateClient("apiservicestaticweather");
        var response = await weatherClient.GetAsync("/infoweather");
        if (!response.IsSuccessStatusCode)
        {
            step2Activity?.SetStatus(ActivityStatusCode.Error, $"Status: {response.StatusCode}");
            stepError = $"HTTP {(int)response.StatusCode} from /infoweather";
        }

        logger.LogInformation("Flow step 2 completed. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
            message.FlowRunId, traceId, message.CorrelationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);
    }
    catch (Exception ex)
    {
        step2Activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        stepError = ex.Message;
        logger.LogError(ex, "Flow step 2 failed. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id}", message.FlowRunId, traceId, message.CorrelationId);
    }

    // Update Step2 to Completed/Failed
    await UpdateFlowStepAsync(message.FlowRunId, "Step2.AsyncProcess",
        stepError is null ? FlowStepStatus.Completed : FlowStepStatus.Failed,
        traceId, spanId, DateTimeOffset.UtcNow, stepError, scopeFactory, logger);

    // Forward Step3 to WorkerService3
    var step3Message = new FlowStepMessage(
        FlowRunId: message.FlowRunId,
        StepName: "Step3.AsyncFinalize",
        TraceParent: Activity.Current?.Id ?? traceParent ?? string.Empty,
        TraceState: Activity.Current?.TraceStateString ?? traceState,
        CorrelationId: message.CorrelationId);

    try
    {
        var ws3Client = httpClientFactory.CreateClient("workerservice3");
        var json = JsonSerializer.Serialize(step3Message);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var ws3Response = await ws3Client.PostAsync("/flow/step", content);
        if (!ws3Response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to forward flow step to workerservice3. status_code={status_code} flow_run_id={flow_run_id}", ws3Response.StatusCode, message.FlowRunId);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception forwarding flow step to workerservice3. flow_run_id={flow_run_id}", message.FlowRunId);
    }
}

static async Task UpdateFlowStepAsync(
    string flowRunId,
    string stepName,
    FlowStepStatus status,
    string? traceId,
    string? spanId,
    DateTimeOffset? completedAt,
    string? errorMessage,
    IServiceScopeFactory scopeFactory,
    ILogger logger)
{
    try
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
        var step = await db.FlowStepRecords
            .FirstOrDefaultAsync(s => s.FlowRunId == flowRunId && s.StepName == stepName);
        if (step is null) return;

        step.Status = status;
        step.TraceId ??= traceId;
        step.SpanId ??= spanId;
        if (status == FlowStepStatus.Running) step.StartedAt = DateTimeOffset.UtcNow;
        step.CompletedAt = completedAt ?? step.CompletedAt;
        step.ErrorMessage = errorMessage ?? step.ErrorMessage;
        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to update flow step. flow_run_id={flow_run_id} step_name={step_name}", flowRunId, stepName);
    }
}

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

// ── Retry-demo Step 2 helper ──────────────────────────────────────────────

static async Task ExecuteRetryStep2Async(
    FlowStepMessage message,
    string? traceParent,
    string? traceState,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    IHostEnvironment hostEnvironment,
    ActivitySource activitySource)
{
    if (!WorkerTraceContext.TryParse(traceParent, traceState, out var parentContext))
    {
        parentContext = default;
    }

    const int maxAttempts = 3;
    const int retryDelaySeconds = 10;

    string? lastError = null;
    string? traceId = null;
    string? spanId = null;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        using var stepActivity = activitySource.StartActivity("RetryStep2.Process", ActivityKind.Consumer, parentContext);
        traceId = Activity.Current?.TraceId.ToString();
        spanId = stepActivity?.SpanId.ToString();

        await UpdateRetryFlowStepAsync(message.FlowRunId, "RetryStep2.Process", FlowStepStatus.Running,
            traceId, spanId, attempt, maxAttempts, null, null, scopeFactory, logger);

        logger.LogInformation("RetryDemo step 2 attempt {attempt}/{max_attempts}. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
            attempt, maxAttempts, message.FlowRunId, traceId, message.CorrelationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);

        if (attempt < maxAttempts)
        {
            lastError = $"Simulerat fel (försök {attempt}/{maxAttempts}) – återförsök om {retryDelaySeconds} s";
            stepActivity?.SetStatus(ActivityStatusCode.Error, lastError);

            logger.LogWarning("RetryDemo step 2 intentional failure on attempt {attempt}/{max_attempts}. error={error} flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                attempt, maxAttempts, lastError, message.FlowRunId, traceId, message.CorrelationId, DateTimeOffset.UtcNow);

            await UpdateRetryFlowStepAsync(message.FlowRunId, "RetryStep2.Process", FlowStepStatus.Retrying,
                traceId, spanId, attempt, maxAttempts, lastError, null, scopeFactory, logger);

            await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
            continue;
        }

        try
        {
            var weatherClient = httpClientFactory.CreateClient("apiservicestaticweather");
            var response = await weatherClient.GetAsync("/infoweather");
            lastError = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} from /infoweather";
            if (!response.IsSuccessStatusCode) stepActivity?.SetStatus(ActivityStatusCode.Error, lastError);
            else logger.LogInformation("RetryDemo step 2 succeeded on attempt {attempt}/{max_attempts}. flow_run_id={flow_run_id} trace_id={trace_id} timestamp_utc={timestamp_utc}",
                attempt, maxAttempts, message.FlowRunId, traceId, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            stepActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            lastError = ex.Message;
        }

        await UpdateRetryFlowStepAsync(message.FlowRunId, "RetryStep2.Process",
            lastError is null ? FlowStepStatus.Completed : FlowStepStatus.Failed,
            traceId, spanId, attempt, maxAttempts, lastError, DateTimeOffset.UtcNow, scopeFactory, logger);

        if (lastError is null)
        {
            // Forward to WS3
            var step3Message = new FlowStepMessage(
                FlowRunId: message.FlowRunId,
                StepName: "RetryStep3.Finalize",
                TraceParent: Activity.Current?.Id ?? traceParent ?? string.Empty,
                TraceState: Activity.Current?.TraceStateString ?? traceState,
                CorrelationId: message.CorrelationId);
            try
            {
                var ws3Client = httpClientFactory.CreateClient("workerservice3");
                var json = System.Text.Json.JsonSerializer.Serialize(step3Message);
                using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var ws3Resp = await ws3Client.PostAsync("/flow/retry-demo/step", content);
                if (!ws3Resp.IsSuccessStatusCode)
                    logger.LogWarning("Failed to forward retry-demo step to workerservice3. status_code={status_code} flow_run_id={flow_run_id}", ws3Resp.StatusCode, message.FlowRunId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception forwarding retry-demo step to workerservice3. flow_run_id={flow_run_id}", message.FlowRunId);
            }
        }
        return;
    }
}

static async Task UpdateRetryFlowStepAsync(
    string flowRunId,
    string stepName,
    FlowStepStatus status,
    string? traceId,
    string? spanId,
    int retryAttempt,
    int maxRetries,
    string? errorMessage,
    DateTimeOffset? completedAt,
    IServiceScopeFactory scopeFactory,
    ILogger logger)
{
    try
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
        var step = await db.FlowStepRecords.FirstOrDefaultAsync(s => s.FlowRunId == flowRunId && s.StepName == stepName);
        if (step is null) return;
        step.Status = status;
        step.TraceId ??= traceId;
        step.SpanId ??= spanId;
        step.RetryAttempt = retryAttempt;
        step.MaxRetries = maxRetries;
        if (status is FlowStepStatus.Running or FlowStepStatus.Retrying && step.StartedAt is null) step.StartedAt = DateTimeOffset.UtcNow;
        if (completedAt.HasValue) step.CompletedAt = completedAt;
        step.ErrorMessage = errorMessage;
        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to update retry-demo step. flow_run_id={flow_run_id} step_name={step_name}", flowRunId, stepName);
    }
}

internal sealed record FlowStepMessage(
    string FlowRunId,
    string StepName,
    string TraceParent,
    string? TraceState,
    string CorrelationId);
