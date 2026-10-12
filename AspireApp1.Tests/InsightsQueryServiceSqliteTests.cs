using AspireApp1.StateStore;
using AspireApp1.Web.Insights;

namespace AspireApp1.Tests;

/// <summary>
/// Verifies that the insights queries run on SQLite (the default provider) and respect the time window.
/// </summary>
[TestClass]
public class InsightsQueryServiceSqliteTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task GetSnapshotAsync_OnSqlite_AggregatesOnlyRowsInsideWindow()
    {
        var factory = await SqliteTestDatabase.CreateFactoryAsync();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.FlowRunRecords.AddRange(
                new FlowRunRecord { FlowRunId = "in", FlowName = "F", CorrelationId = "c1", StartedAt = Now.AddMinutes(-10), Status = FlowRunStatus.Failed },
                new FlowRunRecord { FlowRunId = "out", FlowName = "F", CorrelationId = "c2", StartedAt = Now.AddHours(-3), Status = FlowRunStatus.Completed });
            db.FlowStepRecords.Add(new FlowStepRecord
            {
                FlowRunId = "in", StepName = "Step2", ServiceName = "ws2", StepOrder = 2,
                Status = FlowStepStatus.Failed, StartedAt = Now.AddMinutes(-9), CompletedAt = Now.AddMinutes(-9), ErrorMessage = "boom"
            });
            db.ServiceHealthRecords.Add(new ServiceHealthRecord
            {
                ServiceName = "apiservice", IsHealthy = true, HttpStatusCode = 200, CheckedAt = Now.AddMinutes(-1), CheckedByService = "ws4"
            });
            db.SpanRecords.AddRange(
                new SpanRecord { TraceId = "t", SpanId = "a", ServiceName = "web", OperationName = "op", StartTime = Now.AddMinutes(-5), EndTime = Now.AddMinutes(-5).AddMilliseconds(80), Status = SpanRecordStatus.OK, CreatedAt = Now },
                new SpanRecord { TraceId = "t", SpanId = "b", ParentSpanId = "a", ServiceName = "api", OperationName = "op", StartTime = Now.AddMinutes(-5), EndTime = Now.AddMinutes(-5).AddMilliseconds(40), Status = SpanRecordStatus.OK, CreatedAt = Now });
            db.JobStates.Add(new JobStateRecord { JobId = "j", ServiceName = "ws1", Status = JobStatus.Queued, CorrelationId = "c1", CreatedAt = Now.AddMinutes(-2), UpdatedAt = Now });
            db.ChainRunRecords.Add(new ChainRunRecord { ChainRunId = "chain", CorrelationId = "c1", StartedAt = Now.AddMinutes(-3), Status = ChainRunStatus.Running });
            await db.SaveChangesAsync();
        }

        var sut = new InsightsQueryService(factory, new FixedTimeProvider(Now));
        var snapshot = await sut.GetSnapshotAsync(TimeSpan.FromHours(1));

        Assert.AreEqual(1, snapshot.FlowTrend.Sum(b => b.Total), "Runs older than the window are excluded.");
        Assert.AreEqual("Step2", snapshot.FailureHotspots.Single().StepName);
        Assert.AreEqual(InsightStatus.Ok, snapshot.HealthTimeline.Single().CurrentStatus);
        Assert.AreEqual("web", snapshot.DependencyGraph.Edges.Single().From);
        Assert.AreEqual(2, snapshot.ServiceLatencies.Count);
        Assert.AreEqual(1, snapshot.JobStatuses.Single().Queued);
        Assert.AreEqual("chain", snapshot.ChainRuns.Single().ChainRunId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
