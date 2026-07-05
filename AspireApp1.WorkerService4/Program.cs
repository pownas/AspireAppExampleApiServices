using AspireApp1.StateStore;
using AspireApp1.WorkerService4;

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

builder.AddNpgsqlDbContext<StateStoreDbContext>("statestore");

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
