using Microsoft.AspNetCore.Authentication;

// ==============================================================================
// 1. WEB-API (PRODUKTIONSKOD)
// ==============================================================================
// I en vanlig applikation ligger detta i ditt Web API-projekt (Program.cs).
// Vi inkluderar det här som en klass för att göra exemplet helt självförsörjande.

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Lägg till produktionens autentisering (t.ex. JwtBearer för Entra ID/Auth0)
        builder.Services.AddAuthentication("Bearer")
            .AddJwtBearer(); // Denna kommer vi att ersätta helt under testerna

        builder.Services.AddAuthorization();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        // En skyddad API-slutpunkt som kräver att man är inloggad
        app.MapGet("/api/secure-data", () =>
        {
            return Results.Ok(new
            {
                Message = "Detta är skyddad data!",
                Secret = 42
            });
        })
        .RequireAuthorization();

        app.Run();
    }
}




//var builder = WebApplication.CreateBuilder(args);

//// Add services to the container.
//// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

//var app = builder.Build();

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

//app.UseHttpsRedirection();

//var summaries = new[]
//{
//    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
//};

//app.MapGet("/weatherforecast", () =>
//{
//    var forecast = Enumerable.Range(1, 5).Select(index =>
//        new WeatherForecast
//        (
//            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//            Random.Shared.Next(-20, 55),
//            summaries[Random.Shared.Next(summaries.Length)]
//        ))
//        .ToArray();
//    return forecast;
//})
//.WithName("GetWeatherForecast");

//app.Run();

//internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
//{
//    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
//}



