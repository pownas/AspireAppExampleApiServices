using System.Net;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AspireApp1.Tests;

[TestClass]
public class TraceFlowWebApplicationFactoryTests
{
    [TestMethod]
    public async Task Forecast_WithIncomingTraceContext_PropagatesTraceAndCorrelationToDownstreamCalls()
    {
        const string incomingTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-1111111111111111-01";
        const string incomingCorrelationId = "corr-success-001";

        var staticWeatherRequests = new RequestRecorder();
        var externalServiceRequests = new RequestRecorder();
        var workerRequests = new RequestRecorder();

        await using var factory = CreateFactory(
            staticWeatherResponder: _ => new HttpResponseMessage(HttpStatusCode.OK),
            externalServiceResponder: _ => new HttpResponseMessage(HttpStatusCode.OK),
            workerResponder: _ => new HttpResponseMessage(HttpStatusCode.Accepted),
            staticWeatherRequests,
            externalServiceRequests,
            workerRequests);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/forecast");
        request.Headers.TryAddWithoutValidation("traceparent", incomingTraceParent);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", incomingCorrelationId);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(incomingCorrelationId, response.Headers.GetValues("X-Correlation-Id").Single());

        var expectedTracePrefix = "00-4bf92f3577b34da6a3ce929d0e0e4736-";
        AssertHasTraceParentWithPrefix(staticWeatherRequests.Single(), expectedTracePrefix);
        foreach (var externalRequest in externalServiceRequests.All())
        {
            AssertHasTraceParentWithPrefix(externalRequest, expectedTracePrefix);
        }

        var workerRequest = workerRequests.Single();
        AssertHasTraceParentWithPrefix(workerRequest, expectedTracePrefix);

        var workerPayload = JsonDocument.Parse(workerRequest.Body!);
        Assert.AreEqual(incomingCorrelationId, workerPayload.RootElement.GetProperty("correlationId").GetString());
        var payloadTraceParent = workerPayload.RootElement.GetProperty("traceParent").GetString();
        StringAssert.StartsWith(payloadTraceParent, expectedTracePrefix);
    }

    [TestMethod]
    public async Task Forecast_WhenExternalServiceStatusFails_StillReturnsOkAndQueuesWorkerWithTraceContext()
    {
        const string incomingTraceParent = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-bbbbbbbbbbbbbbbb-01";
        const string incomingCorrelationId = "corr-failure-001";

        var staticWeatherRequests = new RequestRecorder();
        var externalServiceRequests = new RequestRecorder();
        var workerRequests = new RequestRecorder();

        await using var factory = CreateFactory(
            staticWeatherResponder: _ => new HttpResponseMessage(HttpStatusCode.OK),
            externalServiceResponder: request => request.Path.StartsWith("/employeestatus/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK),
            workerResponder: _ => new HttpResponseMessage(HttpStatusCode.Accepted),
            staticWeatherRequests,
            externalServiceRequests,
            workerRequests);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/forecast");
        request.Headers.TryAddWithoutValidation("traceparent", incomingTraceParent);
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", incomingCorrelationId);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(incomingCorrelationId, response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.AreEqual(2, externalServiceRequests.Count);
        Assert.AreEqual(1, workerRequests.Count);

        var expectedTracePrefix = "00-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-";
        AssertHasTraceParentWithPrefix(workerRequests.Single(), expectedTracePrefix);
    }

    private static WebApplicationFactory<ApiServiceForecastWebApplicationFactoryEntryPoint> CreateFactory(
        Func<RecordedRequest, HttpResponseMessage> staticWeatherResponder,
        Func<RecordedRequest, HttpResponseMessage> externalServiceResponder,
        Func<RecordedRequest, HttpResponseMessage> workerResponder,
        RequestRecorder staticWeatherRequests,
        RequestRecorder externalServiceRequests,
        RequestRecorder workerRequests)
    {
        return new WebApplicationFactory<ApiServiceForecastWebApplicationFactoryEntryPoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHttpClientFactory>();
                    services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(CreateFakeClients(
                        staticWeatherRequests,
                        staticWeatherResponder,
                        externalServiceRequests,
                        externalServiceResponder,
                        workerRequests,
                        workerResponder)));
                });
            });
    }

    private static IDictionary<string, HttpClient> CreateFakeClients(
        RequestRecorder staticWeatherRequests,
        Func<RecordedRequest, HttpResponseMessage> staticWeatherResponder,
        RequestRecorder externalServiceRequests,
        Func<RecordedRequest, HttpResponseMessage> externalServiceResponder,
        RequestRecorder workerRequests,
        Func<RecordedRequest, HttpResponseMessage> workerResponder)
    {
        return new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            ["apiservicestaticweather"] = CreateClient(staticWeatherRequests, staticWeatherResponder),
            ["apiexternalservice"] = CreateClient(externalServiceRequests, externalServiceResponder),
            ["workerservice1"] = CreateClient(workerRequests, workerResponder)
        };
    }

    private static HttpClient CreateClient(RequestRecorder recorder, Func<RecordedRequest, HttpResponseMessage> responder)
    {
        var handler = new RecordingHandler(recorder, responder)
        {
            InnerHandler = new HttpClientHandler()
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    private static void AssertHasTraceParentWithPrefix(RecordedRequest request, string tracePrefix)
    {
        Assert.IsNotNull(request.TraceParent);
        StringAssert.StartsWith(request.TraceParent, tracePrefix);
    }

    private sealed class FakeHttpClientFactory(IDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            if (clients.TryGetValue(name, out var client))
            {
                return client;
            }

            throw new InvalidOperationException($"No fake client registered for '{name}'.");
        }
    }

    private sealed class RecordingHandler(
        RequestRecorder recorder,
        Func<RecordedRequest, HttpResponseMessage> responder) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TrySetTraceParentHeaderFromCurrentActivity(request);

            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var recordedRequest = new RecordedRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("traceparent", out var traceParents) ? traceParents.SingleOrDefault() : null,
                request.Headers.TryGetValues("X-Correlation-Id", out var correlationIds) ? correlationIds.SingleOrDefault() : null,
                body);

            recorder.Add(recordedRequest);
            return responder(recordedRequest);
        }

        private static void TrySetTraceParentHeaderFromCurrentActivity(HttpRequestMessage request)
        {
            if (!request.Headers.Contains("traceparent") && !string.IsNullOrWhiteSpace(Activity.Current?.Id))
            {
                request.Headers.TryAddWithoutValidation("traceparent", Activity.Current.Id);
            }
        }
    }

    private sealed class RequestRecorder
    {
        private readonly List<RecordedRequest> _requests = [];

        public int Count => _requests.Count;

        public void Add(RecordedRequest request) => _requests.Add(request);

        public RecordedRequest Single() => _requests.Single();

        public IReadOnlyList<RecordedRequest> All() => _requests;
    }

    private sealed record RecordedRequest(string Path, string? TraceParent, string? CorrelationId, string? Body);
}
