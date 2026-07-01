using System.Diagnostics;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
var activitySource = new ActivitySource("AspireApp1.ApiServiceForecast");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add HttpClientFactories for calling other services
builder.Services.AddHttpClient("apiservicestaticweather", client =>
{
    client.BaseAddress = new Uri("http://apiservicestaticweather");
});

builder.Services.AddHttpClient("apiexternalservice", client =>
{
    client.BaseAddress = new Uri("http://apiexternalservice");
});

builder.Services.AddHttpClient("apierrorservice", client =>
{
    client.BaseAddress = new Uri("http://apierrorservice");
});

builder.Services.AddHttpClient("workerservice1", client =>
{
    client.BaseAddress = new Uri("http://workerservice1");
});


var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseTraceContextLogScope();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /forecast to see sample data.");

app.MapGet("/forecast", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext) =>
{
    var correlationId = httpContext.Items["correlation_id"]?.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

    // Call ApiServiceStaticWeather
    var httpClient = httpClientFactory.CreateClient("apiservicestaticweather");
    try
    {
        using var staticWeatherCallActivity = activitySource.StartActivity("ApiServiceForecast.CallStaticWeather", ActivityKind.Internal);
        var response = await httpClient.GetAsync("/infoweather");
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("ApiServiceStaticWeather response retrieved. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
        else
        {
            staticWeatherCallActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error calling ApiServiceStaticWeather. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }


    // Call apiexternalservice
    var httpClient2 = httpClientFactory.CreateClient("apiexternalservice");
    try
    {
        using var externalServiceCallActivity = activitySource.StartActivity("ApiServiceForecast.CallExternalService", ActivityKind.Internal);
        var employeeId = Random.Shared.Next(1, 7); // Get a random Employee ID between 1 and 7

        var response = await httpClient2.GetAsync($"/employeeinfo/{employeeId}");
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("ApiExternalService employee info retrieved. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }

        var response2 = await httpClient2.GetAsync($"/employeestatus/{employeeId}");
        if (response2.IsSuccessStatusCode)
        {
            logger.LogInformation("ApiExternalService employee status retrieved. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
        else
        {
            externalServiceCallActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response2.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error calling ApiExternalService. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }

    var workerTraceParent = Activity.Current?.Id ?? httpContext.Items["traceparent"]?.ToString() ?? string.Empty;
    var workerTraceState = Activity.Current?.TraceStateString ?? httpContext.Items["tracestate"]?.ToString();
    var workerClient = httpClientFactory.CreateClient("workerservice1");
    var job = new WorkerJobMessage(
        Guid.NewGuid().ToString("N"),
        workerTraceParent,
        workerTraceState,
        correlationId);

    using var workerQueueActivity = activitySource.StartActivity("ApiServiceForecast.QueueWorkerJob", ActivityKind.Producer);
    var workerResponse = await workerClient.PostAsJsonAsync("/jobs", job);
    if (!workerResponse.IsSuccessStatusCode)
    {
        workerQueueActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {workerResponse.StatusCode}");
        logger.LogWarning("Failed to queue worker job. status_code={status_code} trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            workerResponse.StatusCode,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }
    else
    {
        logger.LogInformation("Queued worker job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            job.JobId,
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }

    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetForecast");

app.MapGet("/errorcall", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext) =>
{
    var correlationId = httpContext.Items["correlation_id"]?.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();

    // Call apierrorservice
    var httpClient = httpClientFactory.CreateClient("apierrorservice");
    try
    {
        using var errorFlowActivity = activitySource.StartActivity("ApiServiceForecast.ErrorFlow", ActivityKind.Internal);
        var response = await httpClient.GetAsync("/err");
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Error flow response received from apierrorservice. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
        else
        {
            errorFlowActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
            logger.LogError("Error flow failed in apierrorservice call. status_code={status_code} trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                response.StatusCode,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error flow exception in apierrorservice call. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }

    return forecast;
})
.WithName("GetErrorRequest");

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

internal sealed record WorkerJobMessage(
    string JobId,
    string TraceParent,
    string? TraceState,
    string CorrelationId);
