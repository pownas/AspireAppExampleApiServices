using AspireApp1.StateStore;
using AspireApp1.Web.Insights;

namespace AspireApp1.Tests;

[TestClass]
public class InsightsCalculatorTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddHours(1);

    [TestMethod]
    public void CalculateFailureHotspots_UsesFailedStepOrFirstUnfinishedStepPerFailedRun()
    {
        FlowRunRecord[] runs =
        [
            Run(From.AddMinutes(1), FlowRunStatus.Failed, "run-a", "trace-a"),
            Run(From.AddMinutes(5), FlowRunStatus.Failed, "run-b", "trace-b", "timeout"),
            Run(From.AddMinutes(6), FlowRunStatus.Failed, "run-c", "trace-c"),
            Run(From.AddMinutes(7), FlowRunStatus.Completed, "run-d", "trace-d")
        ];
        FlowStepRecord[] steps =
        [
            Step("run-a", 1, "Step1", "ws1", FlowStepStatus.Completed),
            Step("run-a", 2, "Step2", "ws2", FlowStepStatus.Failed, "boom"),
            Step("run-a", 3, "Step3", "ws3", FlowStepStatus.Pending),
            // Timed out: nothing failed, the run stopped at the first unfinished step.
            Step("run-b", 1, "Step1", "ws1", FlowStepStatus.Completed),
            Step("run-b", 2, "Step2", "ws2", FlowStepStatus.Pending),
            Step("run-c", 1, "Step1", "ws1", FlowStepStatus.Completed),
            Step("run-c", 2, "Step2", "ws2", FlowStepStatus.Completed),
            Step("run-c", 3, "Step3", "ws3", FlowStepStatus.Pending),
            Step("run-d", 1, "Step1", "ws1", FlowStepStatus.Completed)
        ];

        var result = InsightsCalculator.CalculateFailureHotspots(runs, steps);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Step2", result[0].StepName);
        Assert.AreEqual(2, result[0].FailedCount);
        Assert.AreEqual("timeout", result[0].LastError, "Falls back to the run error when the step has none.");
        Assert.AreEqual("trace-b", result[0].LastTraceId);
        Assert.AreEqual("Step3", result[1].StepName);
        Assert.AreEqual(1, result[1].FailedCount);
    }

    [TestMethod]
    public void CalculateHealthTimeline_UsesWorstStatusPerBucketAndComputesUptime()
    {
        ServiceHealthRecord[] checks =
        [
            Health("apiservice", true, 200, From.AddMinutes(1)),
            Health("apiservice", false, 503, From.AddMinutes(2)),
            Health("apiservice", true, 200, From.AddMinutes(40)),
            Health("apiservice", true, 200, From.AddMinutes(41))
        ];

        var rows = InsightsCalculator.CalculateHealthTimeline(checks, From, To, bucketCount: 4);

        Assert.AreEqual(1, rows.Count);
        var cells = rows[0].Cells;
        Assert.AreEqual(4, cells.Count);
        Assert.AreEqual(InsightStatus.Error, cells[0].Status, "A 503 in the bucket should make it red.");
        Assert.AreEqual(1, cells[0].Failures);
        Assert.AreEqual(InsightStatus.NoData, cells[1].Status);
        Assert.AreEqual(InsightStatus.Ok, cells[2].Status);
        Assert.AreEqual(75d, rows[0].UptimePercent);
        Assert.AreEqual(InsightStatus.Ok, rows[0].CurrentStatus);
    }

    [TestMethod]
    public void CalculateHealthTimeline_UnreachableServiceCountsAsError()
    {
        ServiceHealthRecord[] checks = [Health("ws2", false, 0, From.AddMinutes(1))];

        var rows = InsightsCalculator.CalculateHealthTimeline(checks, From, To, bucketCount: 2);

        Assert.AreEqual(InsightStatus.Error, rows[0].CurrentStatus);
    }

    [TestMethod]
    public void CalculateDependencyGraph_DerivesEdgesFromParentSpansAcrossServices()
    {
        SpanRecord[] spans =
        [
            Span("t1", "a1", null, "web", 0, 100, SpanRecordStatus.OK),
            Span("t1", "b1", "a1", "api", 10, 60, SpanRecordStatus.OK),
            Span("t1", "b2", "b1", "api", 20, 30, SpanRecordStatus.OK),
            Span("t1", "c1", "b1", "worker", 30, 50, SpanRecordStatus.Error),
            Span("t2", "a2", null, "web", 0, 100, SpanRecordStatus.OK),
            Span("t2", "b3", "a2", "api", 10, 40, SpanRecordStatus.OK)
        ];

        var graph = InsightsCalculator.CalculateDependencyGraph(spans);

        Assert.AreEqual(2, graph.Edges.Count, "Calls inside the same service are not edges.");
        var webToApi = graph.Edges.Single(e => e.From == "web" && e.To == "api");
        Assert.AreEqual(2, webToApi.Calls);
        Assert.AreEqual(40d, webToApi.AverageMs);
        var apiToWorker = graph.Edges.Single(e => e.From == "api" && e.To == "worker");
        Assert.AreEqual(1, apiToWorker.Errors);

        var layers = graph.Nodes.ToDictionary(n => n.ServiceName, n => n.Layer);
        Assert.AreEqual(0, layers["web"]);
        Assert.AreEqual(1, layers["api"]);
        Assert.AreEqual(2, layers["worker"]);
    }

    [TestMethod]
    public void CalculateDependencyGraph_AddsHandoffsBetweenConsecutiveFlowSteps()
    {
        FlowStepRecord[] steps =
        [
            Step("run-1", 1, "Step1", "ws1", FlowStepStatus.Completed),
            Step("run-1", 2, "Step2", "ws2", FlowStepStatus.Completed),
            Step("run-1", 3, "Step3", "ws3", FlowStepStatus.Failed, "boom"),
            Step("run-2", 1, "Step1", "ws1", FlowStepStatus.Completed),
            Step("run-2", 2, "Step2", "ws2", FlowStepStatus.Completed),
            Step("run-2", 3, "Step3", "ws3", FlowStepStatus.Pending)
        ];

        var graph = InsightsCalculator.CalculateDependencyGraph([], steps);

        Assert.AreEqual(2, graph.Edges.Single(e => e.From == "ws1" && e.To == "ws2").Calls);
        var ws2ToWs3 = graph.Edges.Single(e => e.From == "ws2" && e.To == "ws3");
        Assert.AreEqual(1, ws2ToWs3.Calls, "A pending step was never handed off to.");
        Assert.AreEqual(1, ws2ToWs3.Errors);
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, graph.Nodes.Select(n => n.Layer).ToArray());
    }

    [TestMethod]
    public void CalculateDependencyGraph_WithCycle_Terminates()
    {
        SpanRecord[] spans =
        [
            Span("t1", "a", null, "x", 0, 10, SpanRecordStatus.OK),
            Span("t1", "b", "a", "y", 1, 5, SpanRecordStatus.OK),
            Span("t1", "c", "b", "x", 2, 3, SpanRecordStatus.OK)
        ];

        var graph = InsightsCalculator.CalculateDependencyGraph(spans);

        Assert.AreEqual(2, graph.Nodes.Count);
        Assert.AreEqual(2, graph.Edges.Count);
    }

    [TestMethod]
    public void CalculateFlowTrend_CountsRunsPerBucketByStatus()
    {
        FlowRunRecord[] runs =
        [
            Run(From.AddMinutes(1), FlowRunStatus.Completed),
            Run(From.AddMinutes(2), FlowRunStatus.Failed),
            Run(From.AddMinutes(50), FlowRunStatus.Running),
            Run(From.AddMinutes(-5), FlowRunStatus.Completed)
        ];

        var buckets = InsightsCalculator.CalculateFlowTrend(runs, From, To, bucketCount: 2);

        Assert.AreEqual(2, buckets.Count);
        Assert.AreEqual(1, buckets[0].Completed);
        Assert.AreEqual(1, buckets[0].Failed);
        Assert.AreEqual(1, buckets[1].Running);
        Assert.AreEqual(3, buckets.Sum(b => b.Total), "Runs outside the window are ignored.");
    }

    [TestMethod]
    public void CalculateServiceLatencies_ComputesPercentilesAndErrorCodes()
    {
        var spans = Enumerable.Range(1, 20)
            .Select(i => Span("t", $"s{i}", null, "api", 0, i * 10, i == 20 ? SpanRecordStatus.Error : SpanRecordStatus.OK, i == 20 ? 500 : 200))
            .ToList();

        var result = InsightsCalculator.CalculateServiceLatencies(spans).Single();

        Assert.AreEqual(20, result.SpanCount);
        Assert.AreEqual(100d, result.P50Ms);
        Assert.AreEqual(190d, result.P95Ms);
        Assert.AreEqual(200d, result.MaxMs);
        Assert.AreEqual(1, result.ErrorCount);
        Assert.AreEqual(1, result.StatusCodes[500]);
        Assert.IsFalse(result.StatusCodes.ContainsKey(200));
    }

    [TestMethod]
    public void CalculateRetryStats_CountsRetriedAndFailedExecutions()
    {
        FlowStepRecord[] steps =
        [
            Step("Step2", "ws2", FlowStepStatus.Completed, From, null, null, retryAttempt: 2),
            Step("Step2", "ws2", FlowStepStatus.Failed, From, "x", null, retryAttempt: 3),
            Step("Step2", "ws2", FlowStepStatus.Completed, From, null, null),
            Step("Step2", "ws2", FlowStepStatus.Completed, From, null, null)
        ];

        var result = InsightsCalculator.CalculateRetryStats(steps).Single();

        Assert.AreEqual(4, result.Executions);
        Assert.AreEqual(2, result.WithRetries);
        Assert.AreEqual(5, result.TotalRetries);
        Assert.AreEqual(3, result.MaxRetryAttempt);
        Assert.AreEqual(1, result.FailedAfterRetries);
        Assert.AreEqual(0.5, result.RetryRate);
    }

    [TestMethod]
    public void CalculateJobStatuses_CountsPerServiceAndStatus()
    {
        JobStateRecord[] jobs =
        [
            Job("ws1", JobStatus.Completed),
            Job("ws1", JobStatus.Failed),
            Job("ws1", JobStatus.Queued),
            Job("ws2", JobStatus.Processing)
        ];

        var result = InsightsCalculator.CalculateJobStatuses(jobs);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(3, result[0].Total);
        Assert.AreEqual(1, result[0].Failed);
        Assert.AreEqual(1, result[1].Processing);
    }

    [TestMethod]
    public void CalculateChainRuns_MapsStatusAndDuration()
    {
        ChainRunRecord[] runs =
        [
            new() { ChainRunId = "old", CorrelationId = "c1", StartedAt = From, CompletedAt = From.AddSeconds(2), Status = ChainRunStatus.Completed },
            new() { ChainRunId = "new", CorrelationId = "c2", StartedAt = From.AddMinutes(1), Status = ChainRunStatus.Failed }
        ];

        var result = InsightsCalculator.CalculateChainRuns(runs);

        Assert.AreEqual("new", result[0].ChainRunId);
        Assert.AreEqual(InsightStatus.Error, result[0].Status);
        Assert.IsNull(result[0].Duration);
        Assert.AreEqual(TimeSpan.FromSeconds(2), result[1].Duration);
    }

    [TestMethod]
    public void Percentile_UsesNearestRank()
    {
        double[] values = [1, 2, 3, 4];

        Assert.AreEqual(2d, InsightsCalculator.Percentile(values, 0.5));
        Assert.AreEqual(4d, InsightsCalculator.Percentile(values, 0.95));
        Assert.AreEqual(0d, InsightsCalculator.Percentile([], 0.5));
    }

    private static FlowStepRecord Step(string flowRunId, int order, string name, string service, FlowStepStatus status, string? error = null) => new()
    {
        FlowRunId = flowRunId,
        StepOrder = order,
        StepName = name,
        ServiceName = service,
        Status = status,
        ErrorMessage = error
    };

    private static FlowStepRecord Step(string name, string service, FlowStepStatus status, DateTimeOffset at, string? error, string? traceId, int retryAttempt = 0) => new()
    {
        FlowRunId = Guid.NewGuid().ToString("N"),
        StepName = name,
        ServiceName = service,
        Status = status,
        StartedAt = at,
        CompletedAt = at,
        ErrorMessage = error,
        TraceId = traceId,
        RetryAttempt = retryAttempt
    };

    private static ServiceHealthRecord Health(string service, bool healthy, int code, DateTimeOffset at) => new()
    {
        ServiceName = service,
        IsHealthy = healthy,
        HttpStatusCode = code,
        CheckedAt = at,
        CheckedByService = "workerservice4"
    };

    private static SpanRecord Span(string traceId, string spanId, string? parent, string service, int startMs, int endMs, SpanRecordStatus status, int? code = null) => new()
    {
        TraceId = traceId,
        SpanId = spanId,
        ParentSpanId = parent,
        ServiceName = service,
        OperationName = $"{service}.op",
        StartTime = From.AddMilliseconds(startMs),
        EndTime = From.AddMilliseconds(endMs),
        Status = status,
        HttpStatusCode = code,
        CreatedAt = From
    };

    private static FlowRunRecord Run(DateTimeOffset startedAt, FlowRunStatus status, string? flowRunId = null, string? traceId = null, string? error = null) => new()
    {
        FlowRunId = flowRunId ?? Guid.NewGuid().ToString("N"),
        FlowName = "LongFlow",
        CorrelationId = "c",
        TraceId = traceId,
        StartedAt = startedAt,
        Status = status,
        ErrorMessage = error
    };

    private static JobStateRecord Job(string service, JobStatus status) => new()
    {
        JobId = Guid.NewGuid().ToString("N"),
        ServiceName = service,
        Status = status,
        CorrelationId = "c",
        CreatedAt = From,
        UpdatedAt = From
    };
}
