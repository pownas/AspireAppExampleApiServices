using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var activitySource = new ActivitySource("AspireApp1.ApiService");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add HttpClientFactory for calling other apiServiceForecast
builder.Services.AddHttpClient("apiServiceForecast", client =>
{
    client.BaseAddress = new Uri("http://apiServiceForecast");
});

// Add HttpClientFactory for calling apierrorservice
builder.Services.AddHttpClient("apierrorservice", client =>
{
    client.BaseAddress = new Uri("http://apierrorservice");
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

app.MapGet("/", () => "API service is running. Navigate to /weather to see sample data.");

app.MapGet("/weatherforecast", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext) =>
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

    // Call apiServiceForecast
    var httpClient = httpClientFactory.CreateClient("apiServiceForecast");
    try
    {
        using var callForecastActivity = activitySource.StartActivity("ApiService.CallApiServiceForecast", ActivityKind.Internal);
        var response = await httpClient.GetAsync("/forecast");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("apiServiceForecast response content. response_content={response_content}", content);
            logger.LogInformation("apiServiceForecast response retrieved. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
        else
        {
            callForecastActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error calling apiServiceForecast. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
            Activity.Current?.TraceId.ToString(),
            Activity.Current?.SpanId.ToString(),
            Activity.Current?.ParentSpanId.ToString(),
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow,
            correlationId);
    }

    return forecast;
})
.WithName("GetWeatherForecast");



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

    // Call apiServiceForecast
    var httpClient = httpClientFactory.CreateClient("apiServiceForecast");
    try
    {
        using var errorFlowActivity = activitySource.StartActivity("ApiService.ErrorFlowViaForecast", ActivityKind.Internal);
        var response = await httpClient.GetAsync("/errorcall");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("Error flow response content from apiServiceForecast. response_content={response_content}", content);
            logger.LogInformation("Error flow response received from apiServiceForecast. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
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
            logger.LogError("Error flow failed in apiServiceForecast call. status_code={status_code} trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
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
        logger.LogError(ex, "Error flow exception in apiServiceForecast call. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
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

app.MapGet("/errorcall2", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext) =>
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
        using var errorFlowActivity = activitySource.StartActivity("ApiService.ErrorFlowDirect", ActivityKind.Internal);
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
.WithName("GetErrorRequest2");

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
