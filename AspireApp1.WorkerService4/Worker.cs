namespace AspireApp1.WorkerService4;

using System.Diagnostics;
using System.Net;
using AspireApp1.StateStore;

/// <summary>
/// Periodically polls the health endpoints of WorkerService1, WorkerService2, and WorkerService3,
/// logs their reported status, and persists a <see cref="ServiceHealthRecord"/> to the state store.
/// </summary>
public class StatusMonitor(
    ILogger<StatusMonitor> logger,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment hostEnvironment,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly ActivitySource activitySource = new("AspireApp1.WorkerService4");
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly string[] MonitoredServices = ["workerservice1", "workerservice2", "workerservice3"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Short initial delay to allow dependent services to start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollStatusAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PollStatusAsync(CancellationToken stoppingToken)
    {
        using var pollActivity = activitySource.StartActivity("StatusMonitor.Poll", ActivityKind.Client);
        pollActivity?.SetTag("service.name", hostEnvironment.ApplicationName);

        logger.LogInformation("StatusMonitor polling worker services. service.name={service_name} timestamp_utc={timestamp_utc}",
            hostEnvironment.ApplicationName,
            DateTimeOffset.UtcNow);

        foreach (var serviceName in MonitoredServices)
        {
            await CheckServiceStatusAsync(serviceName, stoppingToken);
        }
    }

    private async Task CheckServiceStatusAsync(string serviceName, CancellationToken stoppingToken)
    {
        using var checkActivity = activitySource.StartActivity($"StatusMonitor.Check.{serviceName}", ActivityKind.Client);
        var traceId = Activity.Current?.TraceId.ToString();

        try
        {
            var httpClient = httpClientFactory.CreateClient(serviceName);
            var response = await httpClient.GetAsync("/health", stoppingToken);
            var statusCode = (int)response.StatusCode;
            var isHealthy = response.IsSuccessStatusCode;

            checkActivity?.SetTag("http.status_code", statusCode);
            checkActivity?.SetTag("monitored.service", serviceName);

            await PersistHealthRecordAsync(serviceName, isHealthy, statusCode, traceId, stoppingToken);

            if (isHealthy)
            {
                logger.LogInformation("StatusMonitor: {monitored_service} is healthy. status_code={status_code} service.name={service_name} timestamp_utc={timestamp_utc}",
                    serviceName,
                    statusCode,
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow);
            }
            else
            {
                checkActivity?.SetStatus(ActivityStatusCode.Error, $"Unhealthy status: {statusCode}");
                logger.LogWarning("StatusMonitor: {monitored_service} is unhealthy. status_code={status_code} service.name={service_name} timestamp_utc={timestamp_utc}",
                    serviceName,
                    statusCode,
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            checkActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await PersistHealthRecordAsync(serviceName, false, (int)HttpStatusCode.ServiceUnavailable, traceId, stoppingToken);
            logger.LogWarning(ex, "StatusMonitor: {monitored_service} returned ServiceUnavailable. service.name={service_name} timestamp_utc={timestamp_utc}",
                serviceName,
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            checkActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await PersistHealthRecordAsync(serviceName, false, 0, traceId, stoppingToken);
            logger.LogError(ex, "StatusMonitor: failed to reach {monitored_service}. service.name={service_name} timestamp_utc={timestamp_utc}",
                serviceName,
                hostEnvironment.ApplicationName,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task PersistHealthRecordAsync(
        string serviceName,
        bool isHealthy,
        int httpStatusCode,
        string? traceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();

            db.ServiceHealthRecords.Add(new ServiceHealthRecord
            {
                ServiceName = serviceName,
                IsHealthy = isHealthy,
                HttpStatusCode = httpStatusCode,
                CheckedAt = DateTimeOffset.UtcNow,
                CheckedByService = hostEnvironment.ApplicationName,
                TraceId = traceId
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist health record. service_name={service_name}", serviceName);
        }
    }
}
