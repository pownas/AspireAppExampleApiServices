namespace AspireApp1.WorkerService1;

using System.Diagnostics;

public class Worker(
    ILogger<Worker> logger,
    IHttpClientFactory httpClientFactory,
    WorkerJobQueue jobQueue,
    IHostEnvironment hostEnvironment) : BackgroundService
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

        for (var retryAttempt = 1; retryAttempt <= MaxRetryAttempts; retryAttempt++)
        {
            using var activity = activitySource.StartActivity("Worker.ProcessJob", ActivityKind.Consumer, parentContext);
            activity?.SetTag("job.id", job.JobId);
            activity?.SetTag("retry.attempt", retryAttempt);
            activity?.SetTag("service.name", hostEnvironment.ApplicationName);

            try
            {
                logger.LogInformation("Worker processing job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempt={retry_attempt}",
                    job.JobId,
                    Activity.Current?.TraceId.ToString(),
                    Activity.Current?.SpanId.ToString(),
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

                logger.LogInformation("Worker completed job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempt={retry_attempt}",
                    job.JobId,
                    Activity.Current?.TraceId.ToString(),
                    Activity.Current?.SpanId.ToString(),
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
                    Activity.Current?.TraceId.ToString(),
                    Activity.Current?.SpanId.ToString(),
                    Activity.Current?.ParentSpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow,
                    job.CorrelationId,
                    retryAttempt);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker final failure (dead-letter) for job {job_id}. trace_id={trace_id} span_id={span_id} parent_span_id={parent_span_id} service.name={service_name} timestamp_utc={timestamp_utc} correlation_id={correlation_id} retry_attempts={retry_attempts}",
                    job.JobId,
                    Activity.Current?.TraceId.ToString(),
                    Activity.Current?.SpanId.ToString(),
                    Activity.Current?.ParentSpanId.ToString(),
                    hostEnvironment.ApplicationName,
                    DateTimeOffset.UtcNow,
                    job.CorrelationId,
                    retryAttempt);
                return;
            }
        }
    }
}
