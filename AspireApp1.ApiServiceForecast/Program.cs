using System.Diagnostics;
using System.Net.Http.Json;
using AspireApp1.StateStore;
using Microsoft.EntityFrameworkCore;

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
    client.BaseAddress = new Uri("https+http://apiservicestaticweather");
});

builder.Services.AddHttpClient("apiexternalservice", client =>
{
    client.BaseAddress = new Uri("https+http://apiexternalservice");
});

builder.Services.AddHttpClient("apierrorservice", client =>
{
    client.BaseAddress = new Uri("https+http://apierrorservice");
});

builder.Services.AddHttpClient("workerservice1", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice1");
});

// State store — used to persist SpanRecords for ProcessFlow visibility
builder.Services.AddDbContext<StateStoreDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("statestore")
        ?? $"Data Source={Path.Combine(Path.GetTempPath(), "AspireApp1StateStore", "statestore.db")}"));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseTraceContextLogScope();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ensure schema (idempotent — also adds any new tables to existing DBs)
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
    await DatabaseInitializer.EnsureSchemaAsync(db);
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /forecast to see sample data.");

app.MapGet("/forecast", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext, StateStoreDbContext db) =>
{
    var correlationId = httpContext.Items["correlation_id"]?.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    var forecastTraceId = Activity.Current?.TraceId.ToString();
    var forecastSpanId = Activity.Current?.SpanId.ToString();

    // Collect span data for ProcessFlow visibility
    var spanRecords = new List<SpanRecord>();

    // --- Call ApiServiceStaticWeather ---
    var httpClient = httpClientFactory.CreateClient("apiservicestaticweather");
    string? sw1SpanId = null, sw1Error = null;
    int? sw1HttpCode = null;
    var sw1Start = DateTimeOffset.UtcNow;
    try
    {
        using var staticWeatherCallActivity = activitySource.StartActivity("ApiServiceForecast.CallStaticWeather", ActivityKind.Internal);
        sw1SpanId = staticWeatherCallActivity?.SpanId.ToString();
        var response = await httpClient.GetAsync("/infoweather");
        sw1HttpCode = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("ApiServiceStaticWeather response content. response_content={response_content}", content);
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
            sw1Error = $"HTTP {(int)response.StatusCode} from /infoweather";
        }
    }
    catch (Exception ex)
    {
        sw1Error = ex.Message;
        logger.LogError(ex, "Error calling ApiServiceStaticWeather. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }
    var sw1End = DateTimeOffset.UtcNow;

    if (forecastTraceId is not null)
    {
        spanRecords.Add(new SpanRecord
        {
            TraceId = forecastTraceId,
            SpanId = sw1SpanId ?? Guid.NewGuid().ToString("N"),
            ParentSpanId = forecastSpanId,
            ServiceName = hostEnvironment.ApplicationName,
            OperationName = "ApiServiceForecast.CallStaticWeather",
            StartTime = sw1Start,
            EndTime = sw1End,
            Status = sw1Error is null ? SpanRecordStatus.OK : SpanRecordStatus.Error,
            ErrorMessage = sw1Error,
            HttpStatusCode = sw1HttpCode,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    // --- Call apiexternalservice ---
    var httpClient2 = httpClientFactory.CreateClient("apiexternalservice");
    string? ext1SpanId = null, ext1Error = null;
    int? ext1HttpCode = null;
    var ext1Start = DateTimeOffset.UtcNow;
    try
    {
        using var externalServiceCallActivity = activitySource.StartActivity("ApiServiceForecast.CallExternalService", ActivityKind.Internal);
        ext1SpanId = externalServiceCallActivity?.SpanId.ToString();
        var employeeId = Random.Shared.Next(1, 7);

        var response = await httpClient2.GetAsync($"/employeeinfo/{employeeId}");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("ApiExternalService employee info response content. response_content={response_content}", content);
            logger.LogInformation("ApiExternalService employee info retrieved. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
        ext1HttpCode = (int)response.StatusCode;

        var response2 = await httpClient2.GetAsync($"/employeestatus/{employeeId}");
        if (response2.IsSuccessStatusCode)
        {
            var content2 = await response2.Content.ReadAsStringAsync();
            logger.LogInformation("ApiExternalService employee status response content. response_content={response_content}", content2);
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
            ext1Error = $"HTTP {(int)response2.StatusCode} from /employeestatus/{employeeId}";
            ext1HttpCode = (int)response2.StatusCode;
        }
    }
    catch (Exception ex)
    {
        ext1Error = ex.Message;
        logger.LogError(ex, "Error calling ApiExternalService. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }
    var ext1End = DateTimeOffset.UtcNow;

    if (forecastTraceId is not null)
    {
        spanRecords.Add(new SpanRecord
        {
            TraceId = forecastTraceId,
            SpanId = ext1SpanId ?? Guid.NewGuid().ToString("N"),
            ParentSpanId = forecastSpanId,
            ServiceName = hostEnvironment.ApplicationName,
            OperationName = "ApiServiceForecast.CallExternalService",
            StartTime = ext1Start,
            EndTime = ext1End,
            Status = ext1Error is null ? SpanRecordStatus.OK : SpanRecordStatus.Error,
            ErrorMessage = ext1Error,
            HttpStatusCode = ext1HttpCode,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    // --- Queue worker job ---
    var workerTraceParent = Activity.Current?.Id ?? httpContext.Items["traceparent"]?.ToString() ?? string.Empty;
    var workerTraceState = Activity.Current?.TraceStateString ?? httpContext.Items["tracestate"]?.ToString();
    var workerClient = httpClientFactory.CreateClient("workerservice1");
    var job = new WorkerJobMessage(
        Guid.NewGuid().ToString("N"),
        workerTraceParent,
        workerTraceState,
        correlationId);

    string? queueSpanId = null, queueError = null;
    int? queueHttpCode = null;
    var queueStart = DateTimeOffset.UtcNow;
    using (var workerQueueActivity = activitySource.StartActivity("ApiServiceForecast.QueueWorkerJob", ActivityKind.Producer))
    {
        queueSpanId = workerQueueActivity?.SpanId.ToString();
        var workerResponse = await workerClient.PostAsJsonAsync("/jobs", job);
        queueHttpCode = (int)workerResponse.StatusCode;
        if (!workerResponse.IsSuccessStatusCode)
        {
            workerQueueActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {workerResponse.StatusCode}");
            queueError = $"HTTP {(int)workerResponse.StatusCode} queuing worker job";
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
            var workerResponseContent = await workerResponse.Content.ReadAsStringAsync();
            logger.LogInformation("Worker job response content. response_content={response_content}", workerResponseContent);
            logger.LogInformation("Queued worker job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                job.JobId,
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
    }
    var queueEnd = DateTimeOffset.UtcNow;

    if (forecastTraceId is not null)
    {
        spanRecords.Add(new SpanRecord
        {
            TraceId = forecastTraceId,
            SpanId = queueSpanId ?? Guid.NewGuid().ToString("N"),
            ParentSpanId = forecastSpanId,
            ServiceName = hostEnvironment.ApplicationName,
            OperationName = "ApiServiceForecast.QueueWorkerJob",
            StartTime = queueStart,
            EndTime = queueEnd,
            Status = queueError is null ? SpanRecordStatus.OK : SpanRecordStatus.Error,
            ErrorMessage = queueError,
            HttpStatusCode = queueHttpCode,
            CreatedAt = DateTimeOffset.UtcNow
        });

        // Persist all span records so they are visible in ProcessFlow
        try
        {
            db.SpanRecords.AddRange(spanRecords);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist span records for trace {TraceId}", forecastTraceId);
        }
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
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Error flow response content from apierrorservice. response_content={response_content}", content);
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

public partial class Program;
public sealed class ApiServiceForecastWebApplicationFactoryEntryPoint;
