using AspireApp1.StateStore;
using AspireApp1.Web;
using AspireApp1.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

// State-store database — used by the TraceQueryService to look up trace data.
builder.AddNpgsqlDbContext<StateStoreDbContext>("statestore");

// TraceQueryService builds TraceModel objects from state-store records written by the worker services.
builder.Services.AddScoped<TraceQueryService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

// Proxy API endpoints — serve trace data from the state store.
app.MapGet("/api/traces/{traceId}", async (string traceId, TraceQueryService queryService, CancellationToken ct) =>
{
    var trace = await queryService.GetByTraceIdAsync(traceId, ct);
    return trace is null ? Results.NotFound() : Results.Ok(trace);
});

app.MapGet("/api/traces/correlation/{correlationId}", async (string correlationId, TraceQueryService queryService, CancellationToken ct) =>
{
    var trace = await queryService.GetByCorrelationIdAsync(correlationId, ct);
    return trace is null ? Results.NotFound() : Results.Ok(trace);
});

app.MapGet("/api/traces/span/{spanId}", async (string spanId, TraceQueryService queryService, CancellationToken ct) =>
{
    var trace = await queryService.GetBySpanIdAsync(spanId, ct);
    return trace is null ? Results.NotFound() : Results.Ok(trace);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
