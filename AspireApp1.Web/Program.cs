using AspireApp1.StateStore;
using AspireApp1.Web;
using AspireApp1.Web.Components;
using Microsoft.EntityFrameworkCore;

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

// HTTP client for triggering flows via WorkerService1
builder.Services.AddHttpClient("workerservice1", client =>
{
    client.BaseAddress = new Uri("https+http://workerservice1");
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

// ── Flow trigger endpoints ──────────────────────────────────────────────────

// Trigger a new multi-step flow via WorkerService1 and return the flowRunId
app.MapPost("/api/flow/start", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("workerservice1");
        var response = await client.PostAsync("/flow/start", content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Flow start failed. status_code={status_code}", response.StatusCode);
            return Results.StatusCode((int)response.StatusCode);
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception triggering flow start");
        return Results.Problem("Failed to start flow");
    }
});

// Poll the status of a flow run (reads directly from state store DB)
app.MapGet("/api/flow/{flowRunId}/status", async (string flowRunId, StateStoreDbContext db, CancellationToken ct) =>
{
    var flowRun = await db.FlowRunRecords
        .FirstOrDefaultAsync(r => r.FlowRunId == flowRunId, ct);

    if (flowRun is null)
    {
        return Results.NotFound();
    }

    var steps = await db.FlowStepRecords
        .Where(s => s.FlowRunId == flowRunId)
        .OrderBy(s => s.StepOrder)
        .ToListAsync(ct);

    return Results.Ok(new
    {
        flowRun.FlowRunId,
        flowRun.FlowName,
        flowRun.CorrelationId,
        flowRun.TraceId,
        Status = flowRun.Status.ToString(),
        flowRun.StartedAt,
        flowRun.CompletedAt,
        flowRun.ErrorMessage,
        Steps = steps.Select(s => new
        {
            s.StepName,
            s.ServiceName,
            s.StepOrder,
            Status = s.Status.ToString(),
            s.StartedAt,
            s.CompletedAt,
            s.ErrorMessage,
            s.TraceId,
            s.SpanId
        })
    });
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
