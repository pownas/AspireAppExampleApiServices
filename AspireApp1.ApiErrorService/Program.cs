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

app.MapGet("/", () => "API error service is running. Navigate to /err to get an error response.");

// This service always returns HTTP error codes - it is a fake service for testing error flows.
app.MapGet("/err", (ILogger<Program> logger) =>
{
    var errorCodes = new[]
    {
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status408RequestTimeout,
        StatusCodes.Status429TooManyRequests,
        StatusCodes.Status500InternalServerError,
        StatusCodes.Status502BadGateway,
        StatusCodes.Status503ServiceUnavailable,
        StatusCodes.Status504GatewayTimeout,
    };

    var statusCode = errorCodes[Random.Shared.Next(errorCodes.Length)];
    logger.LogInformation("Returning error response. status_code={status_code}", statusCode);
    return Results.StatusCode(statusCode);
})
.WithName("GetError");

app.MapDefaultEndpoints();

app.Run();
