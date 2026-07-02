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

// Register the Aspire Dashboard client used by the proxy endpoints and the ProcessFlow page.
var aspireDashboardEndpoint = builder.Configuration["AspireDashboard:Endpoint"] ?? "http://localhost:18888";
builder.Services.AddHttpClient<AspireDashboardClient>(client =>
{
    client.BaseAddress = new Uri(aspireDashboardEndpoint);
});

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

// Proxy API endpoints – forward telemetry requests to the Aspire Dashboard client.
app.MapGet("/api/traces/{traceId}", async (string traceId, AspireDashboardClient client, CancellationToken ct) =>
{
    var trace = await client.GetTraceAsync(traceId, ct);
    return trace is null ? Results.NotFound() : Results.Ok(trace);
});

app.MapGet("/api/traces/correlation/{correlationId}", async (string correlationId, AspireDashboardClient client, CancellationToken ct) =>
{
    var trace = await client.GetByCorrelationIdAsync(correlationId, ct);
    return trace is null ? Results.NotFound() : Results.Ok(trace);
});

app.MapGet("/api/traces/span/{spanId}", async (string spanId, AspireDashboardClient client, CancellationToken ct) =>
{
    var trace = await client.GetTraceBySpanIdAsync(spanId, ct);
    return trace is null ? Results.NotFound() : Results.Ok(trace);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
