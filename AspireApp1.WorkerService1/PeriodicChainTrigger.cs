namespace AspireApp1.WorkerService1;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AspireApp1.ServiceDefaults;
using AspireApp1.StateStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// A background service that periodically initiates a chain of work across worker services.
/// Every cycle it waits 6 seconds before calling WorkerService2, then waits 3 more seconds
/// before calling WorkerService3 directly, creating an observable distributed trace.
/// </summary>
public class PeriodicChainTrigger(
    ILogger<PeriodicChainTrigger> logger,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment hostEnvironment,
    IServiceScopeFactory scopeFactory,
    IOptions<ServiceSettings> settings) : BackgroundService
{
    private static readonly ActivitySource activitySource = new("AspireApp1.WorkerService1.Chain");
    private static readonly TimeSpan TriggerInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait one full interval before the first run so all services have time to start
        await Task.Delay(TriggerInterval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunChainAsync(stoppingToken);
            await Task.Delay(TriggerInterval, stoppingToken);
        }
    }

    private async Task RunChainAsync(CancellationToken stoppingToken)
    {
        using var rootActivity = activitySource.StartActivity("ChainTrigger.Run", ActivityKind.Producer);
        var correlationId = Guid.NewGuid().ToString("N");
        var chainRunId = Guid.NewGuid().ToString("N");
        rootActivity?.SetTag("correlation.id", correlationId);
        rootActivity?.SetTag("chain.run.id", chainRunId);
        rootActivity?.SetTag("service.name", hostEnvironment.ApplicationName);

        var traceId = Activity.Current?.TraceId.ToString();

        if (settings.Value.EnableVerboseStatusLogs)
        {
            logger.LogInformation("Chain trigger started. trace_id={trace_id} span_id={span_id} service.name={service_name} correlation_id={correlation_id} chain_run_id={chain_run_id} timestamp_utc={timestamp_utc}",
                traceId,
                Activity.Current?.SpanId.ToString(),
                hostEnvironment.ApplicationName,
                correlationId,
                chainRunId,
                DateTimeOffset.UtcNow);
        }

        await PersistChainRunAsync(chainRunId, correlationId, traceId, ChainRunStatus.Running, null, stoppingToken);

        try
        {
            // Wait 6 seconds, then call WorkerService2
            await Task.Delay(TimeSpan.FromSeconds(6), stoppingToken);
            await ForwardJobAsync("workerservice2", correlationId, stoppingToken);

            // Wait 3 more seconds, then call WorkerService3 directly
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            await ForwardJobAsync("workerservice3", correlationId, stoppingToken);

            await PersistChainRunAsync(chainRunId, correlationId, traceId, ChainRunStatus.Completed, DateTimeOffset.UtcNow, stoppingToken);

            if (settings.Value.EnableVerboseStatusLogs)
            {
                logger.LogInformation("Chain trigger completed. trace_id={trace_id} span_id={span_id} service.name={service_name} correlation_id={correlation_id} chain_run_id={chain_run_id} timestamp_utc={timestamp_utc}",
                    traceId,
                    Activity.Current?.SpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    correlationId,
                    chainRunId,
                    DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            rootActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await PersistChainRunAsync(chainRunId, correlationId, traceId, ChainRunStatus.Failed, DateTimeOffset.UtcNow, stoppingToken);

            logger.LogError(ex, "Chain trigger failed. trace_id={trace_id} service.name={service_name} correlation_id={correlation_id} chain_run_id={chain_run_id} timestamp_utc={timestamp_utc}",
                traceId,
                hostEnvironment.ApplicationName,
                correlationId,
                chainRunId,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task ForwardJobAsync(string targetService, string correlationId, CancellationToken stoppingToken)
    {
        using var forwardActivity = activitySource.StartActivity($"ChainTrigger.Forward.{targetService}", ActivityKind.Producer);
        var jobId = Guid.NewGuid().ToString("N");
        var jobMessage = new WorkerJobMessage(
            JobId: jobId,
            TraceParent: Activity.Current?.Id ?? string.Empty,
            TraceState: Activity.Current?.TraceStateString,
            CorrelationId: correlationId);

        try
        {
            var httpClient = httpClientFactory.CreateClient(targetService);
            var json = JsonSerializer.Serialize(jobMessage);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("/jobs", content, stoppingToken);

            if (response.IsSuccessStatusCode)
            {
                if (settings.Value.EnableVerboseStatusLogs)
                {
                    logger.LogInformation("Chain trigger forwarded job to {target_service}. job_id={job_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                        targetService,
                        jobId,
                        Activity.Current?.TraceId.ToString(),
                        correlationId,
                        DateTimeOffset.UtcNow);
                }
            }
            else
            {
                forwardActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
                logger.LogWarning("Chain trigger failed to forward job to {target_service}. status_code={status_code} job_id={job_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                    targetService,
                    (int)response.StatusCode,
                    jobId,
                    Activity.Current?.TraceId.ToString(),
                    correlationId,
                    DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            forwardActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Chain trigger exception forwarding job to {target_service}. job_id={job_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                targetService,
                jobId,
                Activity.Current?.TraceId.ToString(),
                correlationId,
                DateTimeOffset.UtcNow);
        }
    }

    private async Task PersistChainRunAsync(
        string chainRunId,
        string correlationId,
        string? traceId,
        ChainRunStatus status,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();

            var existing = await db.ChainRunRecords.FirstOrDefaultAsync(c => c.ChainRunId == chainRunId, cancellationToken);

            if (existing is null)
            {
                db.ChainRunRecords.Add(new ChainRunRecord
                {
                    ChainRunId = chainRunId,
                    CorrelationId = correlationId,
                    TraceId = traceId,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = completedAt,
                    Status = status
                });
            }
            else
            {
                existing.Status = status;
                existing.CompletedAt = completedAt ?? existing.CompletedAt;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist chain run state. chain_run_id={chain_run_id} status={status}", chainRunId, status);
        }
    }
}
