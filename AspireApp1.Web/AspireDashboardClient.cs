using AspireApp1.Web.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace AspireApp1.Web;

/// <summary>
/// Client for fetching telemetry data from the Aspire Dashboard OTLP API.
/// The Aspire Dashboard does not expose a fully public API contract; this client
/// targets the dashboard's resource-service HTTP endpoints where available and
/// falls back gracefully when the endpoint is unreachable.
/// </summary>
public class AspireDashboardClient(HttpClient httpClient, ILogger<AspireDashboardClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Retrieves a trace by its trace ID.
    /// </summary>
    public async Task<TraceModel?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await httpClient.GetFromJsonAsync<TraceModel>(
                $"/api/v1/traces/{traceId}", JsonOptions, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve trace {TraceId} from Aspire Dashboard", traceId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves all traces associated with a correlation ID.
    /// </summary>
    public async Task<TraceModel?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await httpClient.GetFromJsonAsync<TraceModel>(
                $"/api/v1/traces/correlation/{correlationId}", JsonOptions, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve trace by correlation ID {CorrelationId} from Aspire Dashboard", correlationId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves the trace that contains a specific span ID.
    /// </summary>
    public async Task<TraceModel?> GetTraceBySpanIdAsync(string spanId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await httpClient.GetFromJsonAsync<TraceModel>(
                $"/api/v1/traces/span/{spanId}", JsonOptions, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve trace by span ID {SpanId} from Aspire Dashboard", spanId);
            return null;
        }
    }
}
