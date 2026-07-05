namespace AspireApp1.StateStore;

/// <summary>
/// A single health-check snapshot written by WorkerService4's StatusMonitor
/// each time it polls a downstream worker service.
/// </summary>
public sealed class ServiceHealthRecord
{
    public int Id { get; set; }

    /// <summary>Name of the service that was polled (e.g. "workerservice1").</summary>
    public string ServiceName { get; set; } = string.Empty;

    public bool IsHealthy { get; set; }

    /// <summary>HTTP status code returned by the <c>/health</c> endpoint.</summary>
    public int HttpStatusCode { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>Name of the monitoring service (always "workerservice4").</summary>
    public string CheckedByService { get; set; } = string.Empty;

    public string? TraceId { get; set; }
}
