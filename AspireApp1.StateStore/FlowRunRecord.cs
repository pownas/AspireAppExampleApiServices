namespace AspireApp1.StateStore;

/// <summary>
/// Tracks one execution of a named multi-step flow triggered from the frontend.
/// </summary>
public sealed class FlowRunRecord
{
    public int Id { get; set; }

    /// <summary>Unique identifier for this flow run execution.</summary>
    public string FlowRunId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the flow (e.g. "DemoFlow").</summary>
    public string FlowName { get; set; } = string.Empty;

    /// <summary>Correlation identifier shared by all steps in this flow run.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>Distributed trace ID for the flow run.</summary>
    public string? TraceId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the flow is still in progress.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public FlowRunStatus Status { get; set; }

    public string? ErrorMessage { get; set; }
}

public enum FlowRunStatus
{
    Running,
    Completed,
    Failed
}
