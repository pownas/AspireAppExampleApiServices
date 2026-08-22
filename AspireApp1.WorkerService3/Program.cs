using System.Diagnostics;
using AspireApp1.StateStore;
using AspireApp1.WorkerService3;
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

var flowActivitySource = new ActivitySource("AspireApp1.WorkerService3.Flow");

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

// ── Flow step endpoint (Step 3) ────────────────────────────────────────────
// Accepts a flow step message, responds 202 immediately, and processes async.
app.MapPost("/flow/step", (
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
            await ExecuteStep3Async(message, capturedTraceParent, capturedTraceState,
                scopeFactory, httpClientFactory, logger, hostEnvironment, flowActivitySource);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Flow step 3 background task failed. flow_run_id={flow_run_id}", message.FlowRunId);
        }
    });

    return Results.Accepted();
});

// ── Retry-demo flow step endpoint (Step 3) ────────────────────────────────
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
            await ExecuteRetryStep3Async(message, capturedTraceParent, capturedTraceState,
                scopeFactory, httpClientFactory, logger, hostEnvironment, flowActivitySource);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RetryDemo step 3 background task failed. flow_run_id={flow_run_id}", message.FlowRunId);
        }
    });

    return Results.Accepted();
});

app.MapDefaultEndpoints();

app.Run();

static async Task ExecuteStep3Async(
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
        logger.LogWarning("Invalid trace context for flow step 3. flow_run_id={flow_run_id}", message.FlowRunId);
        parentContext = default;
    }

    using var step3Activity = activitySource.StartActivity("FlowStep3.AsyncFinalize", ActivityKind.Consumer, parentContext);
    var traceId = Activity.Current?.TraceId.ToString();
    var spanId = step3Activity?.SpanId.ToString();

    // Update Step3 to Running
    await UpdateFlowStepAsync(message.FlowRunId, "Step3.AsyncFinalize", FlowStepStatus.Running,
        traceId, spanId, null, null, scopeFactory, logger);

    logger.LogInformation("Flow step 3 started. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
        message.FlowRunId, traceId, message.CorrelationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);

    string? stepError = null;
    try
    {
        // Simulate async finalisation with a delay
        await Task.Delay(TimeSpan.FromSeconds(3));

        var weatherClient = httpClientFactory.CreateClient("apiservicestaticweather");
        var response = await weatherClient.GetAsync("/infoweather");
        if (!response.IsSuccessStatusCode)
        {
            step3Activity?.SetStatus(ActivityStatusCode.Error, $"Status: {response.StatusCode}");
            stepError = $"HTTP {(int)response.StatusCode} from /infoweather";
        }

        logger.LogInformation("Flow step 3 completed. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
            message.FlowRunId, traceId, message.CorrelationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);
    }
    catch (Exception ex)
    {
        step3Activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        stepError = ex.Message;
        logger.LogError(ex, "Flow step 3 failed. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id}", message.FlowRunId, traceId, message.CorrelationId);
    }

    var finalStepStatus = stepError is null ? FlowStepStatus.Completed : FlowStepStatus.Failed;
    await UpdateFlowStepAsync(message.FlowRunId, "Step3.AsyncFinalize", finalStepStatus,
        traceId, spanId, DateTimeOffset.UtcNow, stepError, scopeFactory, logger);

    // Mark the overall FlowRun as Completed/Failed
    try
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
        var flowRun = await db.FlowRunRecords
            .FirstOrDefaultAsync(r => r.FlowRunId == message.FlowRunId);
        if (flowRun is not null)
        {
            // Only complete if all steps succeeded (don't override a prior failure)
            var anyFailed = await db.FlowStepRecords
                .AnyAsync(s => s.FlowRunId == message.FlowRunId && s.Status == FlowStepStatus.Failed);

            flowRun.Status = anyFailed ? FlowRunStatus.Failed : FlowRunStatus.Completed;
            flowRun.CompletedAt = DateTimeOffset.UtcNow;
            flowRun.ErrorMessage = anyFailed ? "One or more steps failed" : null;
            await db.SaveChangesAsync();

            logger.LogInformation("FlowRun {flow_run_id} marked as {status}. trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                message.FlowRunId, flowRun.Status, traceId, message.CorrelationId, DateTimeOffset.UtcNow);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to update FlowRun status. flow_run_id={flow_run_id}", message.FlowRunId);
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

// ── Retry-demo Step 3 helper ──────────────────────────────────────────────

static async Task ExecuteRetryStep3Async(
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
        using var stepActivity = activitySource.StartActivity("RetryStep3.Finalize", ActivityKind.Consumer, parentContext);
        traceId = Activity.Current?.TraceId.ToString();
        spanId = stepActivity?.SpanId.ToString();

        await UpdateRetryFlowStep3Async(message.FlowRunId, "RetryStep3.Finalize", FlowStepStatus.Running,
            traceId, spanId, attempt, maxAttempts, null, null, scopeFactory, logger);

        logger.LogInformation("RetryDemo step 3 attempt {attempt}/{max_attempts}. flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} service.name={service_name} timestamp_utc={timestamp_utc}",
            attempt, maxAttempts, message.FlowRunId, traceId, message.CorrelationId, hostEnvironment.ApplicationName, DateTimeOffset.UtcNow);

        if (attempt < maxAttempts)
        {
            lastError = $"Simulerat fel (försök {attempt}/{maxAttempts}) – återförsök om {retryDelaySeconds} s";
            stepActivity?.SetStatus(ActivityStatusCode.Error, lastError);

            logger.LogWarning("RetryDemo step 3 intentional failure on attempt {attempt}/{max_attempts}. error={error} flow_run_id={flow_run_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                attempt, maxAttempts, lastError, message.FlowRunId, traceId, message.CorrelationId, DateTimeOffset.UtcNow);

            await UpdateRetryFlowStep3Async(message.FlowRunId, "RetryStep3.Finalize", FlowStepStatus.Retrying,
                traceId, spanId, attempt, maxAttempts, lastError, null, scopeFactory, logger);

            await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
            continue;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var weatherClient = httpClientFactory.CreateClient("apiservicestaticweather");
            var response = await weatherClient.GetAsync("/infoweather");
            lastError = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} from /infoweather";
            if (!response.IsSuccessStatusCode) stepActivity?.SetStatus(ActivityStatusCode.Error, lastError);
            else logger.LogInformation("RetryDemo step 3 succeeded on attempt {attempt}/{max_attempts}. flow_run_id={flow_run_id} trace_id={trace_id} timestamp_utc={timestamp_utc}",
                attempt, maxAttempts, message.FlowRunId, traceId, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            stepActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            lastError = ex.Message;
        }

        var finalStatus = lastError is null ? FlowStepStatus.Completed : FlowStepStatus.Failed;
        await UpdateRetryFlowStep3Async(message.FlowRunId, "RetryStep3.Finalize", finalStatus,
            traceId, spanId, attempt, maxAttempts, lastError, DateTimeOffset.UtcNow, scopeFactory, logger);

        // Mark the overall flow as Completed/Failed
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
            var flowRun = await db.FlowRunRecords.FirstOrDefaultAsync(r => r.FlowRunId == message.FlowRunId);
            if (flowRun is not null)
            {
                var anyFailed = await db.FlowStepRecords
                    .AnyAsync(s => s.FlowRunId == message.FlowRunId && s.Status == FlowStepStatus.Failed);
                flowRun.Status = anyFailed ? FlowRunStatus.Failed : FlowRunStatus.Completed;
                flowRun.CompletedAt = DateTimeOffset.UtcNow;
                flowRun.ErrorMessage = anyFailed ? "One or more steps failed" : null;
                await db.SaveChangesAsync();
                logger.LogInformation("RetryDemoFlow {flow_run_id} marked as {status}. trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                    message.FlowRunId, flowRun.Status, traceId, message.CorrelationId, DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update RetryDemoFlow run status. flow_run_id={flow_run_id}", message.FlowRunId);
        }
        return;
    }
}

static async Task UpdateRetryFlowStep3Async(
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
        logger.LogWarning(ex, "Failed to update retry-demo step 3. flow_run_id={flow_run_id} step_name={step_name}", flowRunId, stepName);
    }
}

internal sealed record FlowStepMessage(
    string FlowRunId,
    string StepName,
    string TraceParent,
    string? TraceState,
    string CorrelationId);
