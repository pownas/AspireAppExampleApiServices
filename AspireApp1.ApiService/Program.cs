var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add HttpClientFactory for calling other services
builder.Services.AddHttpClient("apiservice2", client =>
{
    client.BaseAddress = new Uri("http://apiservice2");
});

// Add HttpClientFactory for calling other services
builder.Services.AddHttpClient("apierrorservice", client =>
{
    client.BaseAddress = new Uri("http://apierrorservice");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weather to see sample data.");

app.MapGet("/weather", async (IHttpClientFactory httpClientFactory) =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();

    // Call ApiService2
    var httpClient = httpClientFactory.CreateClient("apiservice2");
    try
    {
        var response = await httpClient.GetAsync("/weatherforecast");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"ApiService2 response: {content}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error calling ApiService2: {ex.Message}");
    }

    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/error", async (IHttpClientFactory httpClientFactory) =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();

    // Call ApiService2
    var httpClient = httpClientFactory.CreateClient("apierrorservice");
    try
    {
        var response = await httpClient.GetAsync("/weatherforecast");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"ApiErrorService response: {content}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error calling ApiErrorService: {ex.Message}");
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
