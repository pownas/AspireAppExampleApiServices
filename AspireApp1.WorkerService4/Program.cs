using AspireApp1.StateStore;
using AspireApp1.WorkerService4;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<StatusMonitor>();
builder.Services.AddHttpClient("workerservice1", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice1");
});
builder.Services.AddHttpClient("workerservice2", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice2");
});
builder.Services.AddHttpClient("workerservice3", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice3");
});

builder.Services.AddDbContext<StateStoreDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("statestore")
        ?? $"Data Source={Path.Combine(Path.GetTempPath(), "AspireApp1StateStore", "statestore.db")}"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseTraceContextLogScope();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();
    await DatabaseInitializer.EnsureSchemaAsync(db);
}

app.MapDefaultEndpoints();

app.Run();
