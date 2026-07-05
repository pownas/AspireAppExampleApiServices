namespace AspireApp1.WorkerService1;

using System.Diagnostics;
using AspireApp1.StateStore;
using Microsoft.EntityFrameworkCore;

public class Worker(
    ILogger<Worker> logger,
    IHttpClientFactory httpClientFactory,
    WorkerJobQueue jobQueue,
    IHostEnvironment hostEnvironment,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    private static readonly ActivitySource activitySource = new("AspireApp1.WorkerService1");
    private const int MaxRetryAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in jobQueue.DequeueAllAsync(stoppingToken))
        {
            await ProcessJobWithRetryAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobWithRetryAsync(WorkerJobMessage job, CancellationToken stoppingToken)
    {
        if (!WorkerTraceContext.TryParse(job.TraceParent, job.TraceState, out var parentContext))
        {
            logger.LogWarning("Invalid trace context for worker job {job_id}. traceparent={traceparent} correlation_id={correlation_id}",
                job.JobId,
                job.TraceParent,
                job.CorrelationId);
            return;
        }

        await PersistJobStateAsync(job.JobId, JobStatus.Queued, null, null, job.CorrelationId, null, stoppingToken);

        for (var retryAttempt = 1; retryAttempt <= MaxRetryAttempts; retryAttempt++)
        {
            using var activity = activitySource.StartActivity("Worker.ProcessJob", ActivityKind.Consumer, parentContext);
            activity?.SetTag("job.id", job.JobId);
            activity?.SetTag("retry.attempt", retryAttempt);
            activity?.SetTag("service.name", hostEnvironment.ApplicationName);

            var traceId = Activity.Current?.TraceId.ToString();
            var spanId = Activity.Current?.SpanId.ToString();

            await PersistJobStateAsync(job.JobId, JobStatus.Processing, traceId, spanId, job.CorrelationId, null, stoppingToken);

            try
            {
                logger.LogInformation("Worker processing job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempt={retry_attempt}",
                    job.JobId,
                    traceId,
                    spanId,
                    Activity.Current?.ParentSpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow,
                    job.CorrelationId,
                    retryAttempt);

                using var downstreamActivity = activitySource.StartActivity("Worker.CallStaticWeather", ActivityKind.Client);
                var httpClient = httpClientFactory.CreateClient("apiservicestaticweather");
                var response = await httpClient.GetAsync("/infoweather", stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    downstreamActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
                }
                response.EnsureSuccessStatusCode();

                await PersistJobStateAsync(job.JobId, JobStatus.Completed, traceId, spanId, job.CorrelationId, null, stoppingToken);

                logger.LogInformation("Worker completed job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempt={retry_attempt}",
                    job.JobId,
                    traceId,
                    spanId,
                    Activity.Current?.ParentSpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow,
                    job.CorrelationId,
                    retryAttempt);

                return;
            }
            catch (Exception ex) when (retryAttempt < MaxRetryAttempts)
            {
                logger.LogWarning(ex, "Worker retry for job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempt={retry_attempt}",
                    job.JobId,
                    traceId,
                    spanId,
                    Activity.Current?.ParentSpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow,
                    job.CorrelationId,
                    retryAttempt);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)), stoppingToken);
            }
            catch (Exception ex)
            {
                await PersistJobStateAsync(job.JobId, JobStatus.Failed, traceId, spanId, job.CorrelationId, ex.Message, stoppingToken);

                logger.LogError(ex, "Worker final failure (dead-letter) for job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempts={retry_attempts}",
                    job.JobId,
                    traceId,
                    spanId,
                    Activity.Current?.ParentSpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow,
                    job.CorrelationId,
                    retryAttempt);
                return;
            }
        }
    }

    private async Task PersistJobStateAsync(
        string jobId,
        JobStatus status,
        string? traceId,
        string? spanId,
        string correlationId,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();

            var existing = await db.JobStates.FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);
            var now = DateTimeOffset.UtcNow;

            if (existing is null)
            {
                db.JobStates.Add(new JobStateRecord
                {
                    JobId = jobId,
                    ServiceName = hostEnvironment.ApplicationName,
                    Status = status,
                    TraceId = traceId,
                    SpanId = spanId,
                    CorrelationId = correlationId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ErrorMessage = errorMessage
                });
            }
            else
            {
                existing.Status = status;
                existing.TraceId = traceId ?? existing.TraceId;
                existing.SpanId = spanId ?? existing.SpanId;
                existing.UpdatedAt = now;
                existing.ErrorMessage = status == JobStatus.Completed ? null : (errorMessage ?? existing.ErrorMessage);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist job state. job_id={job_id} status={status}", jobId, status);
        }
    }
}
