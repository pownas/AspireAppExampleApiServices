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

app.MapGet("/", () => "API service is running. Navigate to /employeeinfo or /employeeinfo/{id} to see sample data.");

app.MapGet("/employeeinfo", () =>
{
    return Employees.GetEmployees();
})
.WithName("GetEmployeeInfo");

app.MapGet("/employeeinfo/{id}", (int id) =>
{
    return Employees.GetEmployees().FirstOrDefault(e => e.EmployeeNo == id);
})
.WithName("GetEmployeeInfoById");


app.MapGet("/employeestatus/{id}", async (int id, IHttpClientFactory httpClientFactory) =>
{
    var employee = Employees.GetEmployees().FirstOrDefault(e => e.EmployeeNo == id);
    if (employee == null)
    {
        return Results.NotFound();
    }

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

    EmployeeStatus status = new EmployeeStatus
    (
        EmployeeNo: employee.EmployeeNo,
        PersonAlive: isAlive, // For demonstration purposes, we assume the person is alive
        Status: employee.DateExit.HasValue ? "Inactive" : "Active"
    );

    return Results.Ok(status);
})
.WithName("GetEmployeeStatusById");


app.MapDefaultEndpoints();

app.Run();

record EmployeeStatus(int EmployeeNo, bool PersonAlive, string Status);
record EmployeeInfo(DateOnly DateStart, DateOnly? DateExit, int EmployeeNo, string? Name);

static class Employees
{
    public static EmployeeInfo[] GetEmployees()
    {
        return new[] {
            new EmployeeInfo(DateOnly.FromDateTime(DateTime.Now.AddDays(-2000)), DateOnly.FromDateTime(DateTime.Now.AddDays(-1000)), 1, "John Doe"),
            new EmployeeInfo(DateOnly.FromDateTime(DateTime.Now.AddDays(-1000)), DateOnly.FromDateTime(DateTime.Now.AddDays(-400)), 2, "Jane Smith"),
            new EmployeeInfo(DateOnly.FromDateTime(DateTime.Now.AddDays(-800)), null, 3, "Alice Johnson"),
            new EmployeeInfo(DateOnly.FromDateTime(DateTime.Now.AddDays(-700)), DateOnly.FromDateTime(DateTime.Now.AddDays(-200)), 4, "Bob Brown"),
            new EmployeeInfo(DateOnly.FromDateTime(DateTime.Now.AddDays(-300)), null, 5, "Charlie Davis")
        }.ToArray();
    }
}
