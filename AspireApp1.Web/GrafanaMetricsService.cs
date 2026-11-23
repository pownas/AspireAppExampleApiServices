using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AspireApp1.Web;

public sealed class GrafanaMetricsService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GrafanaMetricsService> logger)
{
    private const string RequestRateQuery = "sum(rate(http_server_request_duration_seconds_count[5m]))";
    private const string P95LatencyQuery =
        "histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m])))";

    public async Task<GrafanaMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken)
    {
        var endpoint = configuration["Grafana:Endpoint"];
        var apiToken = configuration["Grafana:ApiToken"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseAddress)
            || string.IsNullOrWhiteSpace(apiToken))
        {
            throw new GrafanaMetricsUnavailableException();
        }

        try
        {
            var client = httpClientFactory.CreateClient("grafana");
            client.BaseAddress = baseAddress;

            var end = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/ds/query")
            {
                Content = JsonContent.Create(new
                {
                    from = end.AddHours(-1).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    to = end.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                    queries = new[]
                    {
                        CreateQuery("A", RequestRateQuery),
                        CreateQuery("B", P95LatencyQuery)
                    }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

            return new GrafanaMetricsSnapshot(
                ReadSeries(document.RootElement, "A"),
                ReadSeries(document.RootElement, "B"),
                end);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning("Grafana metrics query timed out. error_type={ErrorType}", exception.GetType().Name);
            throw new GrafanaMetricsUnavailableException();
        }
        catch (Exception exception) when (exception is HttpRequestException
            or JsonException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentOutOfRangeException)
        {
            logger.LogWarning(
                "Grafana metrics query failed. error_type={ErrorType} status_code={StatusCode}",
                exception.GetType().Name,
                (exception as HttpRequestException)?.StatusCode);
            throw new GrafanaMetricsUnavailableException();
        }
    }

    private object CreateQuery(string refId, string expression) => new
    {
        refId,
        datasource = new
        {
            uid = configuration["Grafana:DataSourceUid"] ?? "prometheus",
            type = "prometheus"
        },
        expr = expression,
        instant = false,
        range = true,
        format = "time_series",
        intervalMs = 60_000,
        maxDataPoints = 60
    };

    private static IReadOnlyList<MetricPoint> ReadSeries(JsonElement response, string refId)
    {
        if (!response.TryGetProperty("results", out var results)
            || !results.TryGetProperty(refId, out var result))
        {
            return [];
        }

        if (result.TryGetProperty("error", out var error)
            && !string.IsNullOrWhiteSpace(error.GetString()))
        {
            throw new InvalidDataException("Grafana returned a query error.");
        }

        if (!result.TryGetProperty("frames", out var frames))
        {
            return [];
        }

        var points = new List<MetricPoint>();
        foreach (var frame in frames.EnumerateArray())
        {
            if (!frame.TryGetProperty("data", out var data)
                || !data.TryGetProperty("values", out var values)
                || values.GetArrayLength() < 2)
            {
                continue;
            }

            var timeIndex = 0;
            var valueIndex = 1;
            if (frame.TryGetProperty("schema", out var schema)
                && schema.TryGetProperty("fields", out var fields))
            {
                var names = fields.EnumerateArray()
                    .Select(field => field.TryGetProperty("name", out var name) ? name.GetString() : null)
                    .ToArray();
                var discoveredTimeIndex = Array.FindIndex(names, name =>
                    string.Equals(name, "Time", StringComparison.OrdinalIgnoreCase));
                var discoveredValueIndex = Array.FindIndex(names, name =>
                    string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase));
                if (discoveredTimeIndex >= 0 && discoveredValueIndex >= 0)
                {
                    timeIndex = discoveredTimeIndex;
                    valueIndex = discoveredValueIndex;
                }
            }

            var columns = values.EnumerateArray().ToArray();
            if (timeIndex >= columns.Length || valueIndex >= columns.Length)
            {
                continue;
            }

            var timestamps = columns[timeIndex].EnumerateArray().ToArray();
            var samples = columns[valueIndex].EnumerateArray().ToArray();
            for (var index = 0; index < Math.Min(timestamps.Length, samples.Length); index++)
            {
                if (TryReadTimestamp(timestamps[index], out var timestamp)
                    && TryReadValue(samples[index], out var value))
                {
                    points.Add(new MetricPoint(timestamp, value));
                }
            }
        }

        return points;
    }

    private static bool TryReadTimestamp(JsonElement value, out DateTimeOffset timestamp)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var milliseconds))
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            return true;
        }

        return DateTimeOffset.TryParse(
            value.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);
    }

    private static bool TryReadValue(JsonElement value, out double sample)
    {
        var parsed = value.ValueKind == JsonValueKind.Number
            ? value.TryGetDouble(out sample)
            : double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out sample);
        return parsed && double.IsFinite(sample);
    }
}

public sealed record GrafanaMetricsSnapshot(
    IReadOnlyList<MetricPoint> RequestRate,
    IReadOnlyList<MetricPoint> P95Latency,
    DateTimeOffset RetrievedAt);

public sealed record MetricPoint(DateTimeOffset Timestamp, double Value);

public sealed class GrafanaMetricsUnavailableException : Exception;
