namespace AspireApp1.WorkerService2;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

public class Worker(
    ILogger<Worker> logger,
    IHttpClientFactory httpClientFactory,
    WorkerJobQueue jobQueue,
    IHostEnvironment hostEnvironment) : BackgroundService
{
    private static readonly ActivitySource activitySource = new("AspireApp1.WorkerService2");
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
                var weatherClient = httpClientFactory.CreateClient("apiservicestaticweather");
                var weatherResponse = await weatherClient.GetAsync("/infoweather", stoppingToken);
                if (!weatherResponse.IsSuccessStatusCode)
                {
                    downstreamActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {weatherResponse.StatusCode}");
                }
                weatherResponse.EnsureSuccessStatusCode();

                // After 3 seconds, chain the job forward to WorkerService3
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                await ChainToWorkerService3Async(job, stoppingToken);

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

    private async Task ChainToWorkerService3Async(WorkerJobMessage originalJob, CancellationToken stoppingToken)
    {
        using var chainActivity = activitySource.StartActivity("Worker.ChainToWorkerService3", ActivityKind.Producer);
        var chainJob = new WorkerJobMessage(
            JobId: Guid.NewGuid().ToString("N"),
            TraceParent: Activity.Current?.Id ?? string.Empty,
            TraceState: Activity.Current?.TraceStateString,
            CorrelationId: originalJob.CorrelationId);

        try
        {
            var httpClient = httpClientFactory.CreateClient("workerservice3");
            var json = JsonSerializer.Serialize(chainJob);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("/jobs", content, stoppingToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Worker chained job to workerservice3. original_job_id={original_job_id} chain_job_id={chain_job_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                    originalJob.JobId,
                    chainJob.JobId,
                    Activity.Current?.TraceId.ToString(),
                    originalJob.CorrelationId,
                    DateTimeOffset.UtcNow);
            }
            else
            {
                chainActivity?.SetStatus(ActivityStatusCode.Error, $"Status code: {response.StatusCode}");
                logger.LogWarning("Worker failed to chain job to workerservice3. original_job_id={original_job_id} chain_job_id={chain_job_id} status_code={status_code} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                    originalJob.JobId,
                    chainJob.JobId,
                    response.StatusCode,
                    Activity.Current?.TraceId.ToString(),
                    originalJob.CorrelationId,
                    DateTimeOffset.UtcNow);
            }
        }
        catch (Exception ex)
        {
            chainActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Worker exception chaining job to workerservice3. original_job_id={original_job_id} chain_job_id={chain_job_id} trace_id={trace_id} correlation_id={correlation_id} timestamp_utc={timestamp_utc}",
                originalJob.JobId,
                chainJob.JobId,
                Activity.Current?.TraceId.ToString(),
                originalJob.CorrelationId,
                DateTimeOffset.UtcNow);
        }
    }
}
