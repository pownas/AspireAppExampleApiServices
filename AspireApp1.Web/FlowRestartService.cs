using AspireApp1.StateStore;
using Microsoft.EntityFrameworkCore;

namespace AspireApp1.Web;

/// <summary>
/// Handles restart of an existing flow run by starting a new run of the same flow type.
/// </summary>
public sealed class FlowRestartService(
    IDbContextFactory<StateStoreDbContext> dbFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<FlowRestartService> logger)
{
    /// <summary>
    /// Restarts a flow by looking up an existing run and triggering the corresponding WorkerService1 start endpoint.
    /// </summary>
    /// <param name="sourceFlowRunId">The existing flow run id to restart from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing restart status, error details and identifiers for the new run.</returns>
    public async Task<FlowRestartResult> RestartFlowAsync(string sourceFlowRunId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existingFlow = await db.FlowRunRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.FlowRunId == sourceFlowRunId, ct);

        if (existingFlow is null)
        {
            return FlowRestartResult.Failed(StatusCodes.Status404NotFound, "Flödeskörningen kunde inte hittas.");
        }

        var targetPath = string.Equals(existingFlow.FlowName, "RetryDemoFlow", StringComparison.OrdinalIgnoreCase)
            ? "/flow/retry-demo/start"
            : string.Equals(existingFlow.FlowName, "IntermittentDemoFlow", StringComparison.OrdinalIgnoreCase)
                ? "/flow/intermittent-demo/start"
                : "/flow/start";

        try
        {
            var client = httpClientFactory.CreateClient("workerservice1");
            using var response = await client.PostAsync(targetPath, content: null, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Flow restart failed. source_flow_run_id={source_flow_run_id} target_path={target_path} status_code={status_code}",
                    sourceFlowRunId, targetPath, response.StatusCode);
                return FlowRestartResult.Failed((int)response.StatusCode,
                    $"Kunde inte återstarta flödet (HTTP {(int)response.StatusCode}).");
            }

            var payload = ParseRestartPayload(body);
            return FlowRestartResult.Succeeded(
                payload.FlowRunId,
                payload.TraceId,
                payload.CorrelationId);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error restarting flow. source_flow_run_id={source_flow_run_id} target_path={target_path}", sourceFlowRunId, targetPath);
            return FlowRestartResult.Failed(StatusCodes.Status503ServiceUnavailable,
                "Återstart misslyckades: kunde inte nå WorkerService1.");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Timeout restarting flow. source_flow_run_id={source_flow_run_id} target_path={target_path}", sourceFlowRunId, targetPath);
            return FlowRestartResult.Failed(StatusCodes.Status504GatewayTimeout,
                "Återstart misslyckades: timeout vid kontakt med WorkerService1.");
        }
    }

    private static RestartPayload ParseRestartPayload(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new RestartPayload(null, null, null);
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;

            return new RestartPayload(
                root.TryGetProperty("flowRunId", out var flowRunIdElement) ? flowRunIdElement.GetString() : null,
                root.TryGetProperty("traceId", out var traceIdElement) ? traceIdElement.GetString() : null,
                root.TryGetProperty("correlationId", out var correlationIdElement) ? correlationIdElement.GetString() : null);
        }
        catch (System.Text.Json.JsonException)
        {
            return new RestartPayload(null, null, null);
        }
    }

    private sealed record RestartPayload(string? FlowRunId, string? TraceId, string? CorrelationId);
}

/// <summary>
/// Represents the outcome when restarting a flow.
/// </summary>
public sealed record FlowRestartResult(
    bool IsSuccess,
    int StatusCode,
    string? ErrorMessage,
    string? FlowRunId,
    string? TraceId,
    string? CorrelationId)
{
    /// <summary>
    /// Creates a successful restart result.
    /// </summary>
    public static FlowRestartResult Succeeded(string? flowRunId, string? traceId, string? correlationId)
        => new(true, StatusCodes.Status200OK, null, flowRunId, traceId, correlationId);

    /// <summary>
    /// Creates a failed restart result.
    /// </summary>
    public static FlowRestartResult Failed(int statusCode, string errorMessage)
        => new(false, statusCode, errorMessage, null, null, null);
}
