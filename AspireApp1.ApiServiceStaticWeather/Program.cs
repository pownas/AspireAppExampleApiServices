using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var activitySource = new ActivitySource("AspireApp1.ApiServiceStaticWeather");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add HttpClientFactories for calling other services
builder.Services.AddHttpClient("apiserviceperson", client =>
{
    client.BaseAddress = new Uri("http://apiserviceperson");
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

app.MapGet("/", () => "API service is running. Navigate to /infoweather to see sample data.");

app.MapGet("/infoweather", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, IHostEnvironment hostEnvironment, HttpContext httpContext) =>
{
    var correlationId = httpContext.Items["correlation_id"]?.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    var id = Random.Shared.Next(1, 6); // Example employee ID for demonstration purposes

    // Call ApiServicePerson
    bool isAlive = false;
    var httpClient = httpClientFactory.CreateClient("apiserviceperson");
    try
    {
        using var personStatusActivity = activitySource.StartActivity("ApiServiceStaticWeather.CheckPersonStatus", ActivityKind.Internal);
        var response = await httpClient.GetAsync($"/persons/status/{id}");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            isAlive = bool.TryParse(content, out var result) && result;
            logger.LogInformation("ApiServicePerson status response content. response_content={response_content}", content);
            logger.LogInformation("ApiServicePerson status retrieved. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
                Activity.Current?.TraceId.ToString(),
                Activity.Current?.SpanId.ToString(),
                Activity.Current?.ParentSpanId.ToString(),
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow,
                correlationId);
        }
        else
        {
            personStatusActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error calling ApiServicePerson. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id}",
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

    logger.LogInformation("Returning static weather info. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} has_person_status={has_person_status}",
        Activity.Current?.TraceId.ToString(),
        Activity.Current?.SpanId.ToString(),
        Activity.Current?.ParentSpanId.ToString(),
        hostEnvironment.ApplicationName,
        DateTimeOffset.UtcNow,
        correlationId,
        isAlive);

    return forecast;
})
.WithName("GetInfoWeather");

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
