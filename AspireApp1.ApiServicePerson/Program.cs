var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

Person[]? persons = Enumerable.Range(1, 20).Select(index =>
        new Person
        (
            index,
            $"Person {index}",
            Random.Shared.Next(18, 100),
            $"Address {index}",
            $"PhoneNumber {index}",
            Random.Shared.Next(0, 2) == 1,
            $"AdditionalInfo {index}"
        ))
        .ToArray();

app.MapGet("/", () => "API service is running. Navigate to '/persons/info', '/persons/alive', '/persons/status/{id}' to see sample data.");

app.MapGet("/persons/info", () =>
{
    return persons;
})
.WithName("GetPersonInfo");

app.MapGet("/persons/alive", () =>
{
    return persons.Where(p => p.IsAlive);
})
.WithName("GetAllAlivePersons");

app.MapGet("/persons/status/{id}", (int id) =>
{
    var person = persons.FirstOrDefault(p => p.Id == id);
    return person != null ? person.IsAlive : (bool?)null;
})
.WithName("GetPersonStatus");

app.MapDefaultEndpoints();

app.Run();

record Person(int Id, string Name, int Age, string Adress, string PhoneNumber, bool IsAlive, string? AdditionalInfo);
