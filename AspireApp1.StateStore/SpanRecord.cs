namespace AspireApp1.StateStore;

/// <summary>
/// A persisted record of one activity span, written by API services so their
/// spans become searchable in ProcessFlow even without direct OTEL query access.
/// </summary>
public sealed class SpanRecord
{
    public int Id { get; set; }

    public string TraceId { get; set; } = string.Empty;
    public string SpanId { get; set; } = string.Empty;
    public string? ParentSpanId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public SpanRecordStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int? HttpStatusCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum SpanRecordStatus
{
    OK,
    Warning,
    Error
}
