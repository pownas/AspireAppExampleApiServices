using AspireApp1.WorkerService1;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<WorkerJobQueue>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHttpClient("apiservicestaticweather", client =>
{
    client.BaseAddress = new Uri("http://apiservicestaticweather");
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseTraceContextLogScope();

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
