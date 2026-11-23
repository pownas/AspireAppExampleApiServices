using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AspireApp1.Tests;

[TestClass]
public sealed class GrafanaMetricsAuthorizationTests
{
    [TestMethod]
    public async Task MetricsEndpoint_RejectsUnauthenticatedAndNonViewerUsers()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var anonymousResponse = await client.GetAsync("/api/metrics");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/metrics");
        request.Headers.Add("X-Test-Role", "Analyst");
        using var unauthorizedRoleResponse = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, unauthorizedRoleResponse.StatusCode);
    }

    [TestMethod]
    public async Task MetricsEndpoint_ReturnsDataOnlyForAllowedRoleAndKeepsGrafanaTokenServerSide()
    {
        var grafanaHandler = new GrafanaResponseHandler();
        await using var factory = CreateFactory(grafanaHandler);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/metrics");
        request.Headers.Add("X-Test-Role", "Developer");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(grafanaHandler.Authorization);
        Assert.AreEqual("Bearer", grafanaHandler.Authorization.Scheme);
        Assert.AreEqual("test-token", grafanaHandler.Authorization.Parameter);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var points = body.RootElement.GetProperty("requestRate");
        Assert.AreEqual(1, points.GetArrayLength());
        Assert.AreEqual(1.25, points[0].GetProperty("value").GetDouble(), 0.001);
        Assert.AreEqual(0, body.RootElement.GetProperty("p95Latency").GetArrayLength());
        Assert.IsFalse(body.RootElement.ToString().Contains("test-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MetricsEndpoint_HidesGrafanaFailureDetails()
    {
        await using var factory = CreateFactory(new GrafanaResponseHandler(HttpStatusCode.InternalServerError));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/metrics");
        request.Headers.Add("X-Test-Role", "Administrator");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Metrics are temporarily unavailable.");
        Assert.IsFalse(body.Contains("Grafana internal failure", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<AspireApp1.Web.WeatherApiClient> CreateFactory(
        HttpMessageHandler? grafanaHandler = null)
    {
        return new WebApplicationFactory<AspireApp1.Web.WeatherApiClient>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Grafana:Endpoint"] = "http://grafana.test",
                        ["Grafana:ApiToken"] = "test-token"
                    }));
                builder.ConfigureServices(services =>
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.TestScheme;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.TestScheme;
                        options.DefaultForbidScheme = TestAuthenticationHandler.TestScheme;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.TestScheme,
                        _ => { });

                    services.RemoveAll<IHttpClientFactory>();
                    services.AddHttpClient("grafana")
                        .ConfigurePrimaryHttpMessageHandler(() => grafanaHandler ?? new GrafanaResponseHandler());
                });
            });
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string TestScheme = "MetricsTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test-user"), new Claim(ClaimTypes.Role, role.ToString())],
                TestScheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }

    private sealed class GrafanaResponseHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(statusCode == HttpStatusCode.OK
                    ? """{"results":{"A":{"frames":[{"schema":{"fields":[{"name":"Time","type":"time"},{"name":"Value","type":"number"}]},"data":{"values":[["2026-11-23T00:00:00Z"],[1.25]]}}]},"B":{"frames":[{"schema":{"fields":[{"name":"Time","type":"time"},{"name":"Value","type":"number"}]},"data":{"values":[["2026-11-23T00:00:00Z"],["NaN"]]}}]}}}"""
                    : "Grafana internal failure")
            });
        }
    }
}
