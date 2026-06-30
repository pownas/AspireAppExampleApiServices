var builder = WebApplication.CreateBuilder(args);

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


var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /forecast to see sample data.");

app.MapGet("/forecast", async(IHttpClientFactory httpClientFactory) =>
{
    // Call ApiServiceStaticWeather
    var httpClient = httpClientFactory.CreateClient("apiservicestaticweather");
    try
    {
        var response = await httpClient.GetAsync("/infoweather");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"ApiServiceStaticWeather response: {content}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error calling ApiServiceStaticWeather: {ex.Message}");
    }


    // Call apiexternalservice
    var httpClient2 = httpClientFactory.CreateClient("apiexternalservice");
    try
    {
        var employeeId = Random.Shared.Next(1, 7); // Get a random Employee ID between 1 and 7

        var response = await httpClient2.GetAsync($"/employeeinfo/{employeeId}");
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"ApiExternalService response: {content}");
        }

        var response2 = await httpClient2.GetAsync($"/employeestatus/{employeeId}");
        if (response2.IsSuccessStatusCode)
        {
            var content2 = await response2.Content.ReadAsStringAsync();
            Console.WriteLine($"ApiExternalService response: {content2}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error calling ApiExternalService: {ex.Message}");
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

app.MapGet("/errorcall", async (IHttpClientFactory httpClientFactory) =>
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
        var response = await httpClient.GetAsync("/err");
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
