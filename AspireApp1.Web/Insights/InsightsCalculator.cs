using AspireApp1.StateStore;

namespace AspireApp1.Web.Insights;

/// <summary>
/// Pure aggregation logic behind the insights page. Takes already-loaded state-store records
/// and turns them into chart-ready models; has no database or UI dependencies so it can be unit tested.
/// </summary>
public static class InsightsCalculator
{
    /// <summary>
    /// Finds the step each failed flow run stopped at and groups them by step and service.
    /// </summary>
    /// <param name="runs">Flow runs within the analysed window.</param>
    /// <param name="steps">Steps belonging to those runs.</param>
    /// <param name="maxItems">Maximum number of hotspots to return.</param>
    /// <returns>Hotspots ordered by failure count (descending).</returns>
    /// <remarks>
    /// The stop step is the first step with status <see cref="FlowStepStatus.Failed"/>; if none failed
    /// (e.g. the run timed out while a downstream service was down) it is the first step that did not complete.
    /// </remarks>
    public static IReadOnlyList<FailureHotspot> CalculateFailureHotspots(
        IEnumerable<FlowRunRecord> runs, IEnumerable<FlowStepRecord> steps, int maxItems = 10)
    {
        var stepsByRun = steps.GroupBy(s => s.FlowRunId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.StepOrder).ToList());

        return [.. runs
            .Where(r => r.Status == FlowRunStatus.Failed)
            .Select(run =>
            {
                var runSteps = stepsByRun.GetValueOrDefault(run.FlowRunId) ?? [];
                var stop = runSteps.FirstOrDefault(s => s.Status == FlowStepStatus.Failed)
                    ?? runSteps.FirstOrDefault(s => s.Status != FlowStepStatus.Completed);
                return (Run: run, Stop: stop);
            })
            .GroupBy(p => (StepName: p.Stop?.StepName ?? "(okänt steg)", ServiceName: p.Stop?.ServiceName ?? "(okänd tjänst)"))
            .Select(g =>
            {
                var latest = g.OrderByDescending(p => p.Run.StartedAt).First();
                return new FailureHotspot(
                    g.Key.StepName,
                    g.Key.ServiceName,
                    g.Count(),
                    latest.Stop?.ErrorMessage ?? latest.Run.ErrorMessage,
                    latest.Stop?.TraceId ?? latest.Run.TraceId);
            })
            .OrderByDescending(h => h.FailedCount)
            .ThenBy(h => h.StepName, StringComparer.Ordinal)
            .Take(maxItems)];
    }

    /// <summary>
    /// Builds a per-service health timeline with a fixed number of buckets.
    /// </summary>
    /// <param name="checks">Health checks within the window.</param>
    /// <param name="from">Window start.</param>
    /// <param name="to">Window end.</param>
    /// <param name="bucketCount">Number of cells per row.</param>
    /// <returns>One row per service, ordered by name.</returns>
    /// <remarks>Each cell shows the worst status in its bucket so short outages stay visible.</remarks>
    public static IReadOnlyList<HealthTimelineRow> CalculateHealthTimeline(
        IEnumerable<ServiceHealthRecord> checks, DateTimeOffset from, DateTimeOffset to, int bucketCount = 48)
    {
        var bucketSize = GetBucketSize(from, to, bucketCount);

        return [.. checks
            .Where(c => c.CheckedAt >= from && c.CheckedAt < to)
            .GroupBy(c => c.ServiceName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var byBucket = g.GroupBy(c => GetBucketIndex(c.CheckedAt, from, bucketSize, bucketCount))
                    .ToDictionary(b => b.Key, b => b.ToList());

                var cells = Enumerable.Range(0, bucketCount).Select(i =>
                {
                    var start = from + bucketSize * i;
                    if (!byBucket.TryGetValue(i, out var inBucket))
                    {
                        return new HealthCell(start, start + bucketSize, InsightStatus.NoData, 0, 0);
                    }

                    var worst = inBucket.Select(ToHealthStatus).Max();
                    return new HealthCell(start, start + bucketSize, worst, inBucket.Count, inBucket.Count(c => !c.IsHealthy));
                }).ToList();

                var total = g.Count();
                var uptime = total == 0 ? (double?)null : g.Count(c => c.IsHealthy) * 100d / total;
                var current = ToHealthStatus(g.OrderByDescending(c => c.CheckedAt).First());
                return new HealthTimelineRow(g.Key, cells, uptime, current);
            })];
    }

    /// <summary>
    /// Derives a service dependency graph from span parent/child relations and flow-step handoffs.
    /// </summary>
    /// <param name="spans">Spans within the window.</param>
    /// <param name="steps">Flow steps within the window (optional).</param>
    /// <returns>Nodes placed in layers (callers left of callees) and aggregated edges.</returns>
    /// <remarks>
    /// An edge A → B is recorded when a span in service B has a parent span in service A within the same
    /// trace, or when step n of a flow run executed in A and step n+1 in B. Same-service links are ignored.
    /// </remarks>
    public static DependencyGraphModel CalculateDependencyGraph(
        IReadOnlyCollection<SpanRecord> spans, IReadOnlyCollection<FlowStepRecord>? steps = null)
    {
        steps ??= [];
        var bySpanKey = new Dictionary<string, SpanRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var span in spans)
        {
            bySpanKey.TryAdd(SpanKey(span.TraceId, span.SpanId), span);
        }

        var spanLinks = spans
            .Where(s => !string.IsNullOrEmpty(s.ParentSpanId))
            .Select(s => (Child: s, Parent: bySpanKey.GetValueOrDefault(SpanKey(s.TraceId, s.ParentSpanId!))))
            .Where(p => p.Parent is not null)
            .Select(p => new Link(p.Parent!.ServiceName, p.Child.ServiceName, p.Child.Status == SpanRecordStatus.Error, DurationMs(p.Child)));

        var stepLinks = steps
            .GroupBy(s => s.FlowRunId)
            .SelectMany(run =>
            {
                var ordered = run.OrderBy(s => s.StepOrder).ToList();
                return ordered.Zip(ordered.Skip(1))
                    // A pending next step means the handoff never happened (the run stopped before it).
                    .Where(pair => pair.Second.Status != FlowStepStatus.Pending)
                    .Select(pair => new Link(pair.First.ServiceName, pair.Second.ServiceName, pair.Second.Status == FlowStepStatus.Failed, StepDurationMs(pair.Second)));
            });

        var edges = spanLinks.Concat(stepLinks)
            .Where(l => !string.Equals(l.From, l.To, StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => (l.From, l.To))
            .Select(g => new DependencyEdge(
                g.Key.From,
                g.Key.To,
                g.Count(),
                g.Count(l => l.IsError),
                g.Select(l => l.DurationMs).Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty(0).Average()))
            .OrderBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();

        var activities = spans.Select(s => (Service: s.ServiceName, IsError: s.Status == SpanRecordStatus.Error))
            .Concat(steps.Where(s => s.Status != FlowStepStatus.Pending).Select(s => (Service: s.ServiceName, IsError: s.Status == FlowStepStatus.Failed)))
            .ToList();
        var services = activities.Select(a => a.Service).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var layers = AssignLayers(services, edges);

        var nodes = services
            .GroupBy(s => layers[s])
            .SelectMany(layer => layer
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select((service, row) =>
                {
                    var own = activities.Where(a => string.Equals(a.Service, service, StringComparison.OrdinalIgnoreCase)).ToList();
                    return new DependencyNode(service, layer.Key, row, own.Count, own.Count(a => a.IsError));
                }))
            .OrderBy(n => n.Layer)
            .ThenBy(n => n.Row)
            .ToList();

        return new DependencyGraphModel { Nodes = nodes, Edges = edges };
    }

    /// <summary>
    /// Counts flow runs per time bucket, split by outcome.
    /// </summary>
    /// <param name="runs">Flow runs started within the window.</param>
    /// <param name="from">Window start.</param>
    /// <param name="to">Window end.</param>
    /// <param name="bucketCount">Number of buckets.</param>
    /// <returns>Buckets ordered by time, including empty ones.</returns>
    public static IReadOnlyList<FlowTrendBucket> CalculateFlowTrend(
        IEnumerable<FlowRunRecord> runs, DateTimeOffset from, DateTimeOffset to, int bucketCount = 24)
    {
        var bucketSize = GetBucketSize(from, to, bucketCount);
        var byBucket = runs
            .Where(r => r.StartedAt >= from && r.StartedAt < to)
            .GroupBy(r => GetBucketIndex(r.StartedAt, from, bucketSize, bucketCount))
            .ToDictionary(g => g.Key, g => g.ToList());

        return [.. Enumerable.Range(0, bucketCount).Select(i =>
        {
            var start = from + bucketSize * i;
            var inBucket = byBucket.GetValueOrDefault(i) ?? [];
            return new FlowTrendBucket(
                start,
                start + bucketSize,
                inBucket.Count(r => r.Status == FlowRunStatus.Completed),
                inBucket.Count(r => r.Status == FlowRunStatus.Failed),
                inBucket.Count(r => r.Status == FlowRunStatus.Running));
        })];
    }

    /// <summary>
    /// Calculates P50/P95/max duration, error count and HTTP error codes per service.
    /// </summary>
    /// <param name="spans">Spans within the window.</param>
    /// <returns>One entry per service, slowest P95 first.</returns>
    public static IReadOnlyList<ServiceLatency> CalculateServiceLatencies(IEnumerable<SpanRecord> spans) =>
        [.. spans
            .GroupBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var durations = g.Select(DurationMs).Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).ToList();
                var codes = g.Where(s => s.HttpStatusCode is >= 400)
                    .GroupBy(s => s.HttpStatusCode!.Value)
                    .OrderBy(c => c.Key)
                    .ToDictionary(c => c.Key, c => c.Count());
                return new ServiceLatency(
                    g.Key,
                    durations.Count,
                    Percentile(durations, 0.50),
                    Percentile(durations, 0.95),
                    durations.Count == 0 ? 0 : durations[^1],
                    g.Count(s => s.Status == SpanRecordStatus.Error),
                    codes);
            })
            .OrderByDescending(l => l.P95Ms)
            .ThenBy(l => l.ServiceName, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Returns the slowest finished spans.
    /// </summary>
    /// <param name="spans">Spans within the window.</param>
    /// <param name="maxItems">Number of operations to return.</param>
    /// <returns>Slowest operations first.</returns>
    public static IReadOnlyList<SlowOperation> CalculateSlowestOperations(IEnumerable<SpanRecord> spans, int maxItems = 10) =>
        [.. spans
            .Select(s => (Span: s, Ms: DurationMs(s)))
            .Where(p => p.Ms.HasValue)
            .OrderByDescending(p => p.Ms)
            .Take(maxItems)
            .Select(p => new SlowOperation(p.Span.ServiceName, p.Span.OperationName, p.Ms!.Value, p.Span.TraceId, p.Span.StartTime))];

    /// <summary>
    /// Aggregates retry attempts per flow step across all runs.
    /// </summary>
    /// <param name="steps">Flow steps within the window.</param>
    /// <returns>Steps with the highest retry rate first.</returns>
    public static IReadOnlyList<StepRetryStats> CalculateRetryStats(IEnumerable<FlowStepRecord> steps) =>
        [.. steps
            .Where(s => s.Status != FlowStepStatus.Pending)
            .GroupBy(s => (s.StepName, s.ServiceName))
            .Select(g => new StepRetryStats(
                g.Key.StepName,
                g.Key.ServiceName,
                g.Count(),
                g.Count(s => s.RetryAttempt > 0),
                g.Sum(s => s.RetryAttempt),
                g.Max(s => s.RetryAttempt),
                g.Count(s => s.RetryAttempt > 0 && s.Status == FlowStepStatus.Failed)))
            .OrderByDescending(r => r.RetryRate)
            .ThenByDescending(r => r.Executions)
            .ThenBy(r => r.StepName, StringComparer.Ordinal)];

    /// <summary>
    /// Counts jobs per service and status.
    /// </summary>
    /// <param name="jobs">Jobs created within the window.</param>
    /// <returns>One summary per service, ordered by name.</returns>
    public static IReadOnlyList<JobStatusSummary> CalculateJobStatuses(IEnumerable<JobStateRecord> jobs) =>
        [.. jobs
            .GroupBy(j => j.ServiceName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new JobStatusSummary(
                g.Key,
                g.Count(j => j.Status == JobStatus.Queued),
                g.Count(j => j.Status == JobStatus.Processing),
                g.Count(j => j.Status == JobStatus.Completed),
                g.Count(j => j.Status == JobStatus.Failed)))];

    /// <summary>
    /// Maps chain runs to summaries, newest first.
    /// </summary>
    /// <param name="runs">Chain runs within the window.</param>
    /// <param name="maxItems">Maximum number of runs.</param>
    /// <returns>Newest chain runs first.</returns>
    public static IReadOnlyList<ChainRunSummary> CalculateChainRuns(IEnumerable<ChainRunRecord> runs, int maxItems = 20) =>
        [.. runs
            .OrderByDescending(r => r.StartedAt)
            .Take(maxItems)
            .Select(r => new ChainRunSummary(
                r.ChainRunId,
                r.CorrelationId,
                r.TraceId,
                r.Status switch
                {
                    ChainRunStatus.Completed => InsightStatus.Ok,
                    ChainRunStatus.Failed => InsightStatus.Error,
                    _ => InsightStatus.Warning
                },
                r.StartedAt,
                r.CompletedAt - r.StartedAt))];

    /// <summary>
    /// Nearest-rank percentile of an ascending sorted list.
    /// </summary>
    /// <param name="sorted">Values sorted ascending.</param>
    /// <param name="percentile">Percentile between 0 and 1.</param>
    /// <returns>The percentile value, or 0 for an empty list.</returns>
    public static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(percentile * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    /// <summary>
    /// Places each service in a column so that callers appear left of the services they call.
    /// </summary>
    /// <param name="services">All services.</param>
    /// <param name="edges">Observed calls.</param>
    /// <returns>Layer index per service.</returns>
    /// <remarks>Longest-path layering, capped at the number of services so cycles cannot loop forever.</remarks>
    private static Dictionary<string, int> AssignLayers(IReadOnlyList<string> services, IReadOnlyList<DependencyEdge> edges)
    {
        var layers = services.ToDictionary(s => s, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (var iteration = 0; iteration < services.Count; iteration++)
        {
            var changed = false;
            foreach (var edge in edges)
            {
                if (layers.TryGetValue(edge.From, out var fromLayer)
                    && layers.TryGetValue(edge.To, out var toLayer)
                    && toLayer < fromLayer + 1
                    && fromLayer + 1 < services.Count)
                {
                    layers[edge.To] = fromLayer + 1;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        return layers;
    }

    private static InsightStatus ToHealthStatus(ServiceHealthRecord check) =>
        check.IsHealthy ? InsightStatus.Ok
        : check.HttpStatusCode >= 500 || check.HttpStatusCode == 0 ? InsightStatus.Error
        : InsightStatus.Warning;

    private static TimeSpan GetBucketSize(DateTimeOffset from, DateTimeOffset to, int bucketCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketCount, 1);
        var ticks = Math.Max(1, (to - from).Ticks / bucketCount);
        return TimeSpan.FromTicks(ticks);
    }

    private static int GetBucketIndex(DateTimeOffset value, DateTimeOffset from, TimeSpan bucketSize, int bucketCount) =>
        (int)Math.Clamp((value - from).Ticks / bucketSize.Ticks, 0, bucketCount - 1);

    private static double? DurationMs(SpanRecord span) =>
        span.EndTime.HasValue ? Math.Max(0, (span.EndTime.Value - span.StartTime).TotalMilliseconds) : null;

    private static double? StepDurationMs(FlowStepRecord step) =>
        step is { StartedAt: { } start, CompletedAt: { } end } ? Math.Max(0, (end - start).TotalMilliseconds) : null;

    private static string SpanKey(string traceId, string spanId) => $"{traceId}:{spanId}";

    /// <summary>One observed link between two services, before aggregation.</summary>
    private readonly record struct Link(string From, string To, bool IsError, double? DurationMs);
}
