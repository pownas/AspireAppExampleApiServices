namespace AspireApp1.WorkerService1;

using System.Diagnostics;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    private static readonly ActivitySource activitySource = new("AspireApp1.WorkerService1");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            DoWork();
            await Task.Delay(10_000, stoppingToken);
        }
    }

    private void DoWork()
    {
        using var activity = activitySource.StartActivity("DoWork");

        // Digg-standarden spårbarhet och korrelation
        var traceId = activity?.Id ?? Activity.Current?.Id ?? System.Diagnostics.Activity.Current?.TraceId.ToString();
        var spanId = activity?.Id;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("DoWork - TraceID: {traceId}, SpanID: {spanId}, Tid: {time}", 
                traceId, spanId, DateTimeOffset.Now);
        }

        // Din affärscode här
        activity?.SetTag("work.status", "completed");
    }
}
