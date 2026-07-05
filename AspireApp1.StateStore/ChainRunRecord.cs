namespace AspireApp1.StateStore;

/// <summary>
/// Records one execution of the periodic chain initiated by WorkerService1's
/// <c>PeriodicChainTrigger</c>, covering the WS1 → WS2 → WS3 flow.
/// </summary>
public sealed class ChainRunRecord
{
    public int Id { get; set; }

    /// <summary>Unique identifier for this chain execution run.</summary>
    public string ChainRunId { get; set; } = string.Empty;

    /// <summary>Correlation identifier shared by all jobs in this chain run.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the chain is still in progress.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public ChainRunStatus Status { get; set; }
}

public enum ChainRunStatus
{
    Running,
    Completed,
    Failed
}
