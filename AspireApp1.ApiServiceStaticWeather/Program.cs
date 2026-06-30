var builder = WebApplication.CreateBuilder(args);

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /infoweather to see sample data.");

app.MapGet("/infoweather", async (IHttpClientFactory httpClientFactory) =>
{
    var id = Random.Shared.Next(1, 6); // Example employee ID for demonstration purposes

    // Call ApiServicePerson
    bool isAlive = false;
    var httpClient = httpClientFactory.CreateClient("apiserviceperson");
    try
    {
        var response = await httpClient.GetAsync($"/persons/status/{id}");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"ApiServicePerson response: {content}");
            isAlive = bool.TryParse(content, out var result) && result;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error calling ApiServicePerson: {ex.Message}");
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
.WithName("GetInfoWeather");

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
