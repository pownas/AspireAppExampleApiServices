using AspireApp1.StateStore;
using AspireApp1.Web;
using AspireApp1.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var oidcSection = builder.Configuration.GetSection("Authentication:OpenIdConnect");
var oidcAuthority = oidcSection["Authority"];
var oidcClientId = oidcSection["ClientId"];
var oidcClientSecret = oidcSection["ClientSecret"];
var hasOpenIdConnect = !string.IsNullOrWhiteSpace(oidcAuthority)
    && !string.IsNullOrWhiteSpace(oidcClientId)
    && !string.IsNullOrWhiteSpace(oidcClientSecret);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

if (hasOpenIdConnect)
{
    builder.Services.AddAuthentication()
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = oidcAuthority;
            options.ClientId = oidcClientId;
            options.ClientSecret = oidcClientSecret;
            options.ResponseType = "code";
            options.SaveTokens = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
            options.ClaimActions.MapJsonKey(ClaimTypes.Role, "role");
        });
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("MetricsViewer", policy =>
        policy.RequireAuthenticatedUser().RequireRole("Developer", "Administrator"));

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

builder.Services.AddConfiguredStateStoreDbContextFactory(builder.Configuration);

// TraceQueryService builds TraceModel objects from state-store records written by the worker services.
builder.Services.AddScoped<TraceQueryService>();
builder.Services.AddScoped<FlowRestartService>();

// InsightsQueryService aggregates recent state-store data for the /insights page.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AspireApp1.Web.Insights.InsightsQueryService>();
builder.Services.AddHttpClient("grafana", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<GrafanaMetricsService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StateStoreDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await DatabaseInitializer.EnsureSchemaAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapGet("/login", (IConfiguration configuration) =>
{
    var oidc = configuration.GetSection("Authentication:OpenIdConnect");
    if (string.IsNullOrWhiteSpace(oidc["Authority"])
        || string.IsNullOrWhiteSpace(oidc["ClientId"])
        || string.IsNullOrWhiteSpace(oidc["ClientSecret"]))
    {
        return Results.Problem(
            "Sign-in is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/metrics" },
        ["oidc"]);
});

app.MapGet("/access-denied", () =>
    Results.Problem("You do not have permission to view this page.", statusCode: StatusCodes.Status403Forbidden));

app.MapGet("/api/metrics", async (
    GrafanaMetricsService metricsService,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await metricsService.GetMetricsAsync(ct));
    }
    catch (GrafanaMetricsUnavailableException)
    {
        logger.LogWarning("Grafana metrics are temporarily unavailable.");
        return Results.Problem(
            "Metrics are temporarily unavailable.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).RequireAuthorization("MetricsViewer");

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
            s.SpanId,
            s.RetryAttempt,
            s.MaxRetries
        })
    });
});

// Proxy to start the retry-demo flow (WS1 /flow/retry-demo/start)
app.MapPost("/api/flow/retry-demo/start", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("workerservice1");
        var response = await client.PostAsync("/flow/retry-demo/start", content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("RetryDemo flow start failed. status_code={status_code}", response.StatusCode);
            return Results.StatusCode((int)response.StatusCode);
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception triggering retry-demo flow start");
        return Results.Problem("Failed to start retry-demo flow");
    }
});

app.MapPost("/api/flow/intermittent-demo/start", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("workerservice1");
        var response = await client.PostAsync("/flow/intermittent-demo/start", content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("IntermittentDemo flow start failed. status_code={status_code}", response.StatusCode);
            return Results.StatusCode((int)response.StatusCode);
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception triggering intermittent-demo flow start");
        return Results.Problem("Failed to start intermittent-demo flow");
    }
});

app.MapGet("/api/flow/simulation/profiles", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var client = httpClientFactory.CreateClient("workerservice1");
        var response = await client.GetAsync("/flow/simulation/profiles", ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Flow simulation profile fetch failed. status_code={status_code}", response.StatusCode);
            return Results.StatusCode((int)response.StatusCode);
        }
        var body = await response.Content.ReadAsStringAsync(ct);
        return Results.Content(body, "application/json");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception fetching flow simulation profiles");
        return Results.Problem("Failed to fetch flow simulation profiles");
    }
});

// Restart a flow by starting a new run of the same flow type as an existing flowRunId.
app.MapPost("/api/flow/{flowRunId}/restart", async (
    string flowRunId,
    FlowRestartService flowRestartService,
    CancellationToken ct) =>
{
    var restartResult = await flowRestartService.RestartFlowAsync(flowRunId, ct);
    if (restartResult.IsSuccess)
    {
        return Results.Ok(new
        {
            flowRunId = restartResult.FlowRunId,
            traceId = restartResult.TraceId,
            correlationId = restartResult.CorrelationId
        });
    }

    if (restartResult.StatusCode == StatusCodes.Status404NotFound)
    {
        return Results.NotFound(new { error = restartResult.ErrorMessage });
    }

    return Results.Json(
        new { error = restartResult.ErrorMessage ?? "Återstart misslyckades." },
        statusCode: restartResult.StatusCode);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
