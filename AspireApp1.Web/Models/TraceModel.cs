namespace AspireApp1.Web.Models;

public enum SpanStatus
{
    OK,
    Warning,
    Error,
    InProgress,
    Unknown
}

public class LogEntryModel
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = [];
}

public class SpanModel
{
    public string SpanId { get; set; } = string.Empty;
    public string? ParentSpanId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public TimeSpan? Duration { get; set; }
    public SpanStatus Status { get; set; } = SpanStatus.Unknown;
    public string? ErrorMessage { get; set; }
    public int? HttpStatusCode { get; set; }
    public List<LogEntryModel> LogEntries { get; set; } = [];
}

public class TraceModel
{
    public string TraceId { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public SpanStatus OverallStatus { get; set; } = SpanStatus.Unknown;
    public List<SpanModel> Spans { get; set; } = [];
    public DateTimeOffset StartTime { get; set; }
}
