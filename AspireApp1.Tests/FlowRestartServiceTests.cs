using System.Net;
using System.Text;
using AspireApp1.StateStore;
using AspireApp1.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AspireApp1.Tests;

[TestClass]
public class FlowRestartServiceTests
{
    [TestMethod]
    public async Task RestartFlowAsync_WhenWorkerCallSucceeds_ReturnsNewRunIdentifiers()
    {
        var options = new DbContextOptionsBuilder<StateStoreDbContext>()
            .UseInMemoryDatabase($"FlowRestartServiceTests_{Guid.NewGuid():N}")
            .Options;
        var dbFactory = new TestDbContextFactory(options);
        await using (var db = dbFactory.CreateDbContext())
        {
            db.FlowRunRecords.Add(new FlowRunRecord
            {
                FlowRunId = "source-run",
                FlowName = "RetryDemoFlow",
                CorrelationId = "corr-source",
                StartedAt = DateTimeOffset.UtcNow,
                Status = FlowRunStatus.Completed
            });
            await db.SaveChangesAsync();
        }

        var recorder = new RequestRecorder();
        var httpClientFactory = new FakeHttpClientFactory(new HttpClient(new RecordingHandler(recorder, _ =>
        {
            var payload = """
                          {"flowRunId":"new-run-1","traceId":"trace-1","correlationId":"corr-1"}
                          """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://workerservice1.local")
        });

        var sut = new FlowRestartService(dbFactory, httpClientFactory, NullLogger<FlowRestartService>.Instance);

        var result = await sut.RestartFlowAsync("source-run", CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(HttpStatusCode.OK, (HttpStatusCode)result.StatusCode);
        Assert.AreEqual("new-run-1", result.FlowRunId);
        Assert.AreEqual("trace-1", result.TraceId);
        Assert.AreEqual("corr-1", result.CorrelationId);
        Assert.AreEqual("/flow/retry-demo/start", recorder.SinglePath);
    }

    [TestMethod]
    public async Task RestartFlowAsync_WhenWorkerCommunicationFails_ReturnsServiceUnavailable()
    {
        var options = new DbContextOptionsBuilder<StateStoreDbContext>()
            .UseInMemoryDatabase($"FlowRestartServiceTests_{Guid.NewGuid():N}")
            .Options;
        var dbFactory = new TestDbContextFactory(options);
        await using (var db = dbFactory.CreateDbContext())
        {
            db.FlowRunRecords.Add(new FlowRunRecord
            {
                FlowRunId = "source-run",
                FlowName = "DemoFlow",
                CorrelationId = "corr-source",
                StartedAt = DateTimeOffset.UtcNow,
                Status = FlowRunStatus.Failed
            });
            await db.SaveChangesAsync();
        }

        var httpClientFactory = new FakeHttpClientFactory(new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://workerservice1.local")
        });
        var sut = new FlowRestartService(dbFactory, httpClientFactory, NullLogger<FlowRestartService>.Instance);

        var result = await sut.RestartFlowAsync("source-run", CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (HttpStatusCode)result.StatusCode);
        StringAssert.Contains(result.ErrorMessage, "kunde inte nå WorkerService1");
    }

    private sealed class TestDbContextFactory(DbContextOptions<StateStoreDbContext> options) : IDbContextFactory<StateStoreDbContext>
    {
        public StateStoreDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RequestRecorder
    {
        public string? SinglePath { get; private set; }

        public void Record(string path)
        {
            SinglePath = path;
        }
    }

    private sealed class RecordingHandler(RequestRecorder recorder, Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            recorder.Record(request.RequestUri?.AbsolutePath ?? string.Empty);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("boom");
    }
}
