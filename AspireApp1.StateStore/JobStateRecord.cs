namespace AspireApp1.StateStore;

/// <summary>
/// Persisted lifecycle state for a single worker job,
/// tracked from enqueue through completion or failure.
/// </summary>
public sealed class JobStateRecord
{
    public int Id { get; set; }

    /// <summary>Unique job identifier (matches <c>WorkerJobMessage.JobId</c>).</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>Name of the service that owns this job (e.g. "workerservice1").</summary>
    public string ServiceName { get; set; } = string.Empty;

    public JobStatus Status { get; set; }

    public string? TraceId { get; set; }
    public string? SpanId { get; set; }

    /// <summary>Correlation identifier propagated across the whole chain.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }
}

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed
}
