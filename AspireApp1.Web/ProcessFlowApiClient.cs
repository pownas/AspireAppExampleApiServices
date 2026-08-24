using AspireApp1.Web.Models;
using System.Net.Http.Json;

namespace AspireApp1.Web;

/// <summary>
/// HTTP client for the internal process-flow proxy API endpoints exposed by this application.
/// </summary>
public class ProcessFlowApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Retrieves a trace by its trace ID (full or partial, minimum 16 hex characters).
    /// </summary>
    public Task<TraceModel?> GetByTraceIdAsync(string traceId, CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<TraceModel>($"/api/traces/{traceId}", cancellationToken);

    /// <summary>
    /// Retrieves a trace by its correlation ID.
    /// </summary>
    public Task<TraceModel?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<TraceModel>($"/api/traces/correlation/{correlationId}", cancellationToken);

    /// <summary>
    /// Retrieves the trace that contains a specific span ID.
    /// </summary>
    public Task<TraceModel?> GetBySpanIdAsync(string spanId, CancellationToken cancellationToken = default) =>
        httpClient.GetFromJsonAsync<TraceModel>($"/api/traces/span/{spanId}", cancellationToken);
}
