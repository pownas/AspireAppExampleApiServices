namespace AspireApp1.Web.Insights;

/// <summary>Status used by the insight visualizations (maps to the fixed status palette).</summary>
public enum InsightStatus
{
    /// <summary>No data in the period.</summary>
    NoData,
    /// <summary>Healthy / successful.</summary>
    Ok,
    /// <summary>Degraded or partially failing.</summary>
    Warning,
    /// <summary>Failing.</summary>
    Error
}

/// <summary>All aggregated insight data for one time window.</summary>
public sealed class InsightsSnapshot
{
    /// <summary>Start of the analysed window (inclusive).</summary>
    public DateTimeOffset From { get; init; }
    /// <summary>End of the analysed window (exclusive).</summary>
    public DateTimeOffset To { get; init; }
    /// <summary>Where failed flows stopped, most frequent first.</summary>
    public IReadOnlyList<FailureHotspot> FailureHotspots { get; init; } = [];
    /// <summary>Health history per service.</summary>
    public IReadOnlyList<HealthTimelineRow> HealthTimeline { get; init; } = [];
    /// <summary>Service dependency graph derived from spans.</summary>
    public DependencyGraphModel DependencyGraph { get; init; } = new();
    /// <summary>Flow runs per time bucket.</summary>
    public IReadOnlyList<FlowTrendBucket> FlowTrend { get; init; } = [];
    /// <summary>Latency and error statistics per service.</summary>
    public IReadOnlyList<ServiceLatency> ServiceLatencies { get; init; } = [];
    /// <summary>The slowest individual operations.</summary>
    public IReadOnlyList<SlowOperation> SlowestOperations { get; init; } = [];
    /// <summary>Retry statistics per flow step.</summary>
    public IReadOnlyList<StepRetryStats> RetryStats { get; init; } = [];
    /// <summary>Job queue status per service.</summary>
    public IReadOnlyList<JobStatusSummary> JobStatuses { get; init; } = [];
    /// <summary>Most recent chain runs.</summary>
    public IReadOnlyList<ChainRunSummary> ChainRuns { get; init; } = [];
}

/// <summary>A step where failed flow runs stopped.</summary>
/// <param name="StepName">Name of the failing step.</param>
/// <param name="ServiceName">Service that executed the step.</param>
/// <param name="FailedCount">Number of failed executions in the window.</param>
/// <param name="LastError">Most recent error message, if any.</param>
/// <param name="LastTraceId">Trace of the most recent failure (for drill-down), if known.</param>
public sealed record FailureHotspot(string StepName, string ServiceName, int FailedCount, string? LastError, string? LastTraceId);

/// <summary>Health history for one service.</summary>
/// <param name="ServiceName">Monitored service.</param>
/// <param name="Cells">One cell per time bucket, oldest first.</param>
/// <param name="UptimePercent">Share of healthy checks in the window, or null without checks.</param>
/// <param name="CurrentStatus">Status of the latest check.</param>
public sealed record HealthTimelineRow(string ServiceName, IReadOnlyList<HealthCell> Cells, double? UptimePercent, InsightStatus CurrentStatus);

/// <summary>Aggregated health checks in one time bucket.</summary>
/// <param name="Start">Bucket start.</param>
/// <param name="End">Bucket end.</param>
/// <param name="Status">Worst status in the bucket.</param>
/// <param name="Checks">Number of checks.</param>
/// <param name="Failures">Number of unhealthy checks.</param>
public sealed record HealthCell(DateTimeOffset Start, DateTimeOffset End, InsightStatus Status, int Checks, int Failures);

/// <summary>Service dependency graph.</summary>
public sealed class DependencyGraphModel
{
    /// <summary>Services, with their column (layer) in a left-to-right layout.</summary>
    public IReadOnlyList<DependencyNode> Nodes { get; init; } = [];
    /// <summary>Observed calls between services.</summary>
    public IReadOnlyList<DependencyEdge> Edges { get; init; } = [];
}

/// <summary>A service in the dependency graph.</summary>
/// <param name="ServiceName">Service name.</param>
/// <param name="Layer">Column index (0 = entry point).</param>
/// <param name="Row">Row index within the column.</param>
/// <param name="ActivityCount">Number of spans and flow steps executed by the service.</param>
/// <param name="ErrorCount">Number of error spans and failed steps.</param>
public sealed record DependencyNode(string ServiceName, int Layer, int Row, int ActivityCount, int ErrorCount)
{
    /// <summary>Share of failed activities, 0-1.</summary>
    public double ErrorRate => ActivityCount == 0 ? 0 : (double)ErrorCount / ActivityCount;
}

/// <summary>Calls (spans) or flow handoffs (consecutive steps) observed from one service to another.</summary>
/// <param name="From">Calling / handing-off service.</param>
/// <param name="To">Called / receiving service.</param>
/// <param name="Calls">Number of calls or handoffs.</param>
/// <param name="Errors">Number that ended in error.</param>
/// <param name="AverageMs">Average duration of the called span or receiving step.</param>
public sealed record DependencyEdge(string From, string To, int Calls, int Errors, double AverageMs)
{
    /// <summary>Share of failed calls, 0-1.</summary>
    public double ErrorRate => Calls == 0 ? 0 : (double)Errors / Calls;
}

/// <summary>Flow runs started in one time bucket.</summary>
/// <param name="Start">Bucket start.</param>
/// <param name="End">Bucket end.</param>
/// <param name="Completed">Completed runs.</param>
/// <param name="Failed">Failed runs.</param>
/// <param name="Running">Runs still in progress.</param>
public sealed record FlowTrendBucket(DateTimeOffset Start, DateTimeOffset End, int Completed, int Failed, int Running)
{
    /// <summary>Total runs in the bucket.</summary>
    public int Total => Completed + Failed + Running;
}

/// <summary>Latency statistics for one service.</summary>
/// <param name="ServiceName">Service name.</param>
/// <param name="SpanCount">Number of finished spans.</param>
/// <param name="P50Ms">Median duration.</param>
/// <param name="P95Ms">95th percentile duration.</param>
/// <param name="MaxMs">Longest duration.</param>
/// <param name="ErrorCount">Number of error spans.</param>
/// <param name="StatusCodes">Count per HTTP status code (>= 400 only).</param>
public sealed record ServiceLatency(string ServiceName, int SpanCount, double P50Ms, double P95Ms, double MaxMs, int ErrorCount, IReadOnlyDictionary<int, int> StatusCodes)
{
    /// <summary>Share of error spans, 0-1.</summary>
    public double ErrorRate => SpanCount == 0 ? 0 : (double)ErrorCount / SpanCount;
}

/// <summary>One slow operation.</summary>
/// <param name="ServiceName">Service name.</param>
/// <param name="OperationName">Operation (span) name.</param>
/// <param name="DurationMs">Duration.</param>
/// <param name="TraceId">Trace for drill-down.</param>
/// <param name="StartTime">When the span started.</param>
public sealed record SlowOperation(string ServiceName, string OperationName, double DurationMs, string TraceId, DateTimeOffset StartTime);

/// <summary>Retry behaviour of one flow step across runs.</summary>
/// <param name="StepName">Step name.</param>
/// <param name="ServiceName">Service that runs the step.</param>
/// <param name="Executions">Number of executions.</param>
/// <param name="WithRetries">Executions that needed at least one retry.</param>
/// <param name="TotalRetries">Sum of retry attempts.</param>
/// <param name="MaxRetryAttempt">Highest retry attempt seen.</param>
/// <param name="FailedAfterRetries">Executions that failed after retrying.</param>
public sealed record StepRetryStats(string StepName, string ServiceName, int Executions, int WithRetries, int TotalRetries, int MaxRetryAttempt, int FailedAfterRetries)
{
    /// <summary>Share of executions that needed a retry, 0-1.</summary>
    public double RetryRate => Executions == 0 ? 0 : (double)WithRetries / Executions;
}

/// <summary>Job queue status for one service.</summary>
/// <param name="ServiceName">Service name.</param>
/// <param name="Queued">Queued jobs.</param>
/// <param name="Processing">Jobs being processed.</param>
/// <param name="Completed">Completed jobs.</param>
/// <param name="Failed">Failed jobs.</param>
public sealed record JobStatusSummary(string ServiceName, int Queued, int Processing, int Completed, int Failed)
{
    /// <summary>Total jobs.</summary>
    public int Total => Queued + Processing + Completed + Failed;
}

/// <summary>One chain run.</summary>
/// <param name="ChainRunId">Chain run id.</param>
/// <param name="CorrelationId">Correlation id.</param>
/// <param name="TraceId">Trace id, if known.</param>
/// <param name="Status">Run status.</param>
/// <param name="StartedAt">Start time.</param>
/// <param name="Duration">Duration if completed.</param>
public sealed record ChainRunSummary(string ChainRunId, string CorrelationId, string? TraceId, InsightStatus Status, DateTimeOffset StartedAt, TimeSpan? Duration);
