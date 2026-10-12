using AspireApp1.StateStore;
using AspireApp1.Web;
using AspireApp1.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AspireApp1.Tests;

[TestClass]
public class TraceQueryServiceTests
{
    [TestMethod]
    public void HasSpans_ReturnsFalseForNullOrEmptyTrace()
    {
        Assert.IsFalse(TraceQueryService.HasSpans(null));
        Assert.IsFalse(TraceQueryService.HasSpans(new TraceModel()));
    }

    [TestMethod]
    public void HasSpans_ReturnsTrueForTraceWithSpans()
    {
        Assert.IsTrue(TraceQueryService.HasSpans(new TraceModel
        {
            Spans = [new() { SpanId = "span" }]
        }));
    }

    [TestMethod]
    public async Task GetByTraceIdAsync_WhenInputIsAspireTraceDetailUrl_FindsTrace()
    {
        const string traceId = "c2e459604c1fb3f710ce788062e93418";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.SpanRecords.Add(new SpanRecord
            {
                TraceId = traceId.ToUpperInvariant(),
                SpanId = "1111111111111111",
                ServiceName = "AspireApp1.ApiService",
                OperationName = "ApiService.CallApiServiceForecast",
                StartTime = DateTimeOffset.UtcNow.AddSeconds(-2),
                EndTime = DateTimeOffset.UtcNow,
                Status = SpanRecordStatus.OK,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetByTraceIdAsync($"https://aspireapp1.dev.localhost:17127/traces/detail/{traceId}");

        Assert.IsNotNull(result);
        Assert.AreEqual(traceId, result.TraceId);
        Assert.AreEqual(1, result.Spans.Count);
    }

    [TestMethod]
    public async Task GetByTraceIdWithFallbackAsync_WhenTraceIdMisses_FindsCorrelationIdTrace()
    {
        const string lookupId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.FlowRunRecords.Add(new FlowRunRecord
            {
                FlowRunId = "fallback-correlation",
                FlowName = "CorrelationFallback",
                CorrelationId = lookupId,
                StartedAt = DateTimeOffset.UtcNow,
                Status = FlowRunStatus.Completed
            });
            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetByTraceIdWithFallbackAsync(lookupId, lookupId, null);

        Assert.IsNotNull(result);
        Assert.AreEqual("fallback-correlation", result.FlowRuns.Single().FlowRunId);
        Assert.IsTrue(TraceQueryService.HasSpans(result));
    }

    [TestMethod]
    public async Task GetByTraceIdWithFallbackAsync_WhenTraceAndCorrelationMiss_FindSpanIdTrace()
    {
        const string lookupId = "ffffffffffffffffffffffffffffffff";
        const string fallbackTraceId = "abababababababababababababababab";
        const string spanId = "3333333333333333";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.SpanRecords.Add(new SpanRecord
            {
                TraceId = fallbackTraceId,
                SpanId = spanId,
                ServiceName = "AspireApp1.ApiService",
                OperationName = "ApiService.Call",
                StartTime = DateTimeOffset.UtcNow.AddSeconds(-1),
                EndTime = DateTimeOffset.UtcNow,
                Status = SpanRecordStatus.OK,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetByTraceIdWithFallbackAsync(lookupId, lookupId, spanId);

        Assert.IsNotNull(result);
        Assert.AreEqual(fallbackTraceId, result.TraceId);
        Assert.IsTrue(TraceQueryService.HasSpans(result));
    }

    [TestMethod]
    public async Task GetByTraceIdAsync_WhenFlowFailed_MapsStopStepAsStepXOfN()
    {
        const string traceId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string flowRunId = "flow-run-01";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.FlowRunRecords.Add(new FlowRunRecord
            {
                FlowRunId = flowRunId,
                FlowName = "LongFlow",
                CorrelationId = "corr-1",
                TraceId = traceId,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = FlowRunStatus.Failed,
                ErrorMessage = "One or more steps failed"
            });

            db.FlowStepRecords.AddRange(
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 1,
                    StepName = "Step1",
                    ServiceName = "AspireApp1.WorkerService1",
                    Status = FlowStepStatus.Completed,
                    TraceId = traceId
                },
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 2,
                    StepName = "Step2",
                    ServiceName = "AspireApp1.WorkerService2",
                    Status = FlowStepStatus.Failed,
                    ErrorMessage = "Timeout in WorkerService2",
                    TraceId = traceId
                },
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 3,
                    StepName = "Step3",
                    ServiceName = "AspireApp1.WorkerService3",
                    Status = FlowStepStatus.Pending
                });

            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetByTraceIdAsync(traceId);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.FlowRuns.Count);
        var flow = result.FlowRuns[0];
        Assert.AreEqual("Fel", flow.Status);
        Assert.AreEqual(2, flow.CurrentStep);
        Assert.AreEqual(3, flow.TotalSteps);
        Assert.AreEqual(1, flow.LastSuccessStep);
        Assert.AreEqual(2, flow.ErrorStep);
        Assert.AreEqual("AspireApp1.WorkerService2", flow.CurrentService);
        Assert.AreEqual("Timeout in WorkerService2", flow.ErrorMessage);
    }

    [TestMethod]
    public async Task GetByTraceIdAsync_IncludesPendingStepsWithoutTraceIdForMatchedFlowRun()
    {
        const string traceId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string flowRunId = "flow-run-02";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.FlowRunRecords.Add(new FlowRunRecord
            {
                FlowRunId = flowRunId,
                FlowName = "LongFlow",
                CorrelationId = "corr-2",
                TraceId = traceId,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = FlowRunStatus.Running
            });

            db.FlowStepRecords.AddRange(
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 1,
                    StepName = "Step1",
                    ServiceName = "AspireApp1.WorkerService1",
                    Status = FlowStepStatus.Completed,
                    TraceId = traceId
                },
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 2,
                    StepName = "Step2",
                    ServiceName = "AspireApp1.WorkerService2",
                    Status = FlowStepStatus.Pending
                },
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 3,
                    StepName = "Step3",
                    ServiceName = "AspireApp1.WorkerService3",
                    Status = FlowStepStatus.Pending
                });

            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetByTraceIdAsync(traceId);

        Assert.IsNotNull(result);
        var flow = result.FlowRuns.Single();
        Assert.AreEqual(3, flow.TotalSteps);
        Assert.AreEqual(1, flow.CurrentStep);
        Assert.AreEqual("Pågår", flow.Status);
        Assert.AreEqual("AspireApp1.WorkerService2", flow.CurrentService);
    }

    [TestMethod]
    public async Task GetByTraceIdAsync_WhenOnlyFlowRunHasTraceId_ReturnsFlowRunAndSteps()
    {
        const string traceId = "cccccccccccccccccccccccccccccccc";
        const string flowRunId = "flow-run-03";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.FlowRunRecords.Add(new FlowRunRecord
            {
                FlowRunId = flowRunId,
                FlowName = "FlowOnlyTraceOnRun",
                CorrelationId = "corr-3",
                TraceId = traceId,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                Status = FlowRunStatus.Running
            });

            db.FlowStepRecords.AddRange(
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 1,
                    StepName = "Step1",
                    ServiceName = "AspireApp1.WorkerService1",
                    Status = FlowStepStatus.Completed
                },
                new FlowStepRecord
                {
                    FlowRunId = flowRunId,
                    StepOrder = 2,
                    StepName = "Step2",
                    ServiceName = "AspireApp1.WorkerService2",
                    Status = FlowStepStatus.Pending
                });

            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetByTraceIdAsync(traceId);

        Assert.IsNotNull(result);
        var flow = result.FlowRuns.Single();
        Assert.AreEqual("FlowOnlyTraceOnRun", flow.FlowName);
        Assert.AreEqual(2, flow.TotalSteps);
        Assert.AreEqual("Pågår", flow.Status);
        Assert.AreEqual(1, flow.CurrentStep);
        Assert.AreEqual("AspireApp1.WorkerService2", flow.CurrentService);
    }

    [TestMethod]
    public async Task GetBySpanIdAsync_WhenSpanExistsInSpanRecords_ReturnsTrace()
    {
        const string traceId = "dddddddddddddddddddddddddddddddd";
        const string spanId = "2222222222222222";
        var factory = CreateFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.SpanRecords.Add(new SpanRecord
            {
                TraceId = traceId,
                SpanId = spanId.ToUpperInvariant(),
                ServiceName = "AspireApp1.ApiServiceForecast",
                OperationName = "ApiServiceForecast.Process",
                StartTime = DateTimeOffset.UtcNow.AddSeconds(-5),
                EndTime = DateTimeOffset.UtcNow,
                Status = SpanRecordStatus.OK,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var sut = new TraceQueryService(factory, NullLogger<TraceQueryService>.Instance);
        var result = await sut.GetBySpanIdAsync(spanId);

        Assert.IsNotNull(result);
        Assert.AreEqual(traceId, result.TraceId);
        Assert.AreEqual(1, result.Spans.Count);
    }

    private static IDbContextFactory<StateStoreDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<StateStoreDbContext>()
            .UseInMemoryDatabase($"TraceQueryServiceTests_{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private sealed class TestDbContextFactory(DbContextOptions<StateStoreDbContext> options) : IDbContextFactory<StateStoreDbContext>
    {
        public StateStoreDbContext CreateDbContext() => new(options);
    }
}
