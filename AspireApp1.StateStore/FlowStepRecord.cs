namespace AspireApp1.StateStore;

/// <summary>
/// Tracks the status of one step within a <see cref="FlowRunRecord"/>.
/// </summary>
public sealed class FlowStepRecord
{
    public int Id { get; set; }

    /// <summary>References the owning <see cref="FlowRunRecord.FlowRunId"/>.</summary>
    public string FlowRunId { get; set; } = string.Empty;

    /// <summary>Human-readable step name (e.g. "Step1.SyncValidate").</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Name of the service executing this step.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>1-based ordering of this step within the flow.</summary>
    public int StepOrder { get; set; }

    public FlowStepStatus Status { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Null while the step is still in progress.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Trace ID recorded when the step starts.</summary>
    public string? TraceId { get; set; }

    /// <summary>Span ID of the activity recorded when the step runs.</summary>
    public string? SpanId { get; set; }

    /// <summary>Current retry attempt number (1-based). 0 = not yet started.</summary>
    public int RetryAttempt { get; set; }

    /// <summary>Maximum number of retry attempts configured for this step.</summary>
    public int MaxRetries { get; set; }
}

public enum FlowStepStatus
{
    Pending,
    Running,
    Retrying,
    Completed,
    Failed
}
