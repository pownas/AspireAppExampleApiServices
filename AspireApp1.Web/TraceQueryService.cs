using AspireApp1.StateStore;
using AspireApp1.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace AspireApp1.Web;

/// <summary>
/// Builds <see cref="TraceModel"/> objects from the distributed-state-store records
/// (job states, chain runs, service health, flow runs, flow steps, span records)
/// written by the worker and API services.
/// Supports lookup by traceId, W3C traceparent string, correlationId, and spanId.
/// </summary>
public class TraceQueryService(IDbContextFactory<StateStoreDbContext> dbFactory, ILogger<TraceQueryService> logger)
{
    // W3C traceparent: 00-{traceId:32hex}-{parentId:16hex}-{flags:2hex}
    private static readonly Regex TraceParentRegex = new(
        @"^00-([0-9a-fA-F]{32})-([0-9a-fA-F]{16})-[0-9a-fA-F]{2}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Retrieves a trace by its 32-character hex trace ID or a full W3C traceparent string.
    /// </summary>
    public async Task<TraceModel?> GetByTraceIdAsync(string traceId, CancellationToken cancellationToken = default)
    {
        using var db = dbFactory.CreateDbContext();

        var input = traceId.Trim();

        // Auto-detect traceparent format and extract traceId
        if (TryParseTraceParent(input, out var parsedTraceId, out _))
        {
            input = parsedTraceId;
        }

        var normalizedId = input.ToLowerInvariant();

        var jobs = await db.JobStates
            .Where(j => j.TraceId != null && j.TraceId.ToLower() == normalizedId)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        var chainRuns = await db.ChainRunRecords
            .Where(c => c.TraceId != null && c.TraceId.ToLower() == normalizedId)
            .OrderBy(c => c.StartedAt)
            .ToListAsync(cancellationToken);

        var healthRecords = await db.ServiceHealthRecords
            .Where(h => h.TraceId != null && h.TraceId.ToLower() == normalizedId)
            .OrderBy(h => h.CheckedAt)
            .ToListAsync(cancellationToken);

        var flowSteps = await db.FlowStepRecords
            .Where(s => s.TraceId != null && s.TraceId.ToLower() == normalizedId)
            .OrderBy(s => s.StepOrder)
            .ToListAsync(cancellationToken);

        var flowRunIds = flowSteps.Select(s => s.FlowRunId).Distinct().ToList();
        var flowRuns = flowRunIds.Count > 0
            ? await db.FlowRunRecords
                .Where(r => flowRunIds.Contains(r.FlowRunId))
                .ToListAsync(cancellationToken)
            : [];

        var spanRecords = await db.SpanRecords
            .Where(s => s.TraceId != null && s.TraceId.ToLower() == normalizedId)
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "TraceQuery by traceId={TraceId}: jobs={JobCount} chainRuns={ChainCount} healthRecords={HealthCount} flowRuns={FlowRunCount} flowSteps={FlowStepCount} spanRecords={SpanCount}",
            normalizedId, jobs.Count, chainRuns.Count, healthRecords.Count, flowRuns.Count, flowSteps.Count, spanRecords.Count);

        return BuildTraceModel(normalizedId, null, jobs, chainRuns, healthRecords, flowRuns, flowSteps, spanRecords);
    }

    /// <summary>
    /// Retrieves all traces associated with a correlation ID.
    /// </summary>
    public async Task<TraceModel?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        using var db = dbFactory.CreateDbContext();

        var normalizedId = correlationId.Trim();

        var jobs = await db.JobStates
            .Where(j => j.CorrelationId == normalizedId)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

        var chainRuns = await db.ChainRunRecords
            .Where(c => c.CorrelationId == normalizedId)
            .OrderBy(c => c.StartedAt)
            .ToListAsync(cancellationToken);

        var flowRuns = await db.FlowRunRecords
            .Where(r => r.CorrelationId == normalizedId)
            .OrderBy(r => r.StartedAt)
            .ToListAsync(cancellationToken);

        var flowRunIds = flowRuns.Select(r => r.FlowRunId).ToList();
        var flowSteps = flowRunIds.Count > 0
            ? await db.FlowStepRecords
                .Where(s => flowRunIds.Contains(s.FlowRunId))
                .OrderBy(s => s.StepOrder)
                .ToListAsync(cancellationToken)
            : [];

        logger.LogInformation(
            "TraceQuery by correlationId={CorrelationId}: jobs={JobCount} chainRuns={ChainCount} flowRuns={FlowRunCount}",
            normalizedId, jobs.Count, chainRuns.Count, flowRuns.Count);

        if (jobs.Count == 0 && chainRuns.Count == 0 && flowRuns.Count == 0)
        {
            return null;
        }

        // Use the first available traceId from the matched records
        var traceId = jobs.FirstOrDefault(j => j.TraceId is not null)?.TraceId
            ?? chainRuns.FirstOrDefault(c => c.TraceId is not null)?.TraceId
            ?? flowRuns.FirstOrDefault(r => r.TraceId is not null)?.TraceId
            ?? normalizedId;

        // Also load any health records and span records for the same traceId
        var healthRecords = await db.ServiceHealthRecords
            .Where(h => h.TraceId != null && h.TraceId.ToLower() == traceId.ToLower())
            .OrderBy(h => h.CheckedAt)
            .ToListAsync(cancellationToken);

        var spanRecords = await db.SpanRecords
            .Where(s => s.TraceId != null && s.TraceId.ToLower() == traceId.ToLower())
            .OrderBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        return BuildTraceModel(traceId, normalizedId, jobs, chainRuns, healthRecords, flowRuns, flowSteps, spanRecords);
    }

    /// <summary>
    /// Retrieves the trace that contains a specific span ID.
    /// </summary>
    public async Task<TraceModel?> GetBySpanIdAsync(string spanId, CancellationToken cancellationToken = default)
    {
        using var db = dbFactory.CreateDbContext();

        var normalizedId = spanId.Trim().ToLowerInvariant();

        // Check job states first
        var job = await db.JobStates
            .Where(j => j.SpanId != null && j.SpanId.ToLower() == normalizedId)
            .FirstOrDefaultAsync(cancellationToken);

        if (job?.TraceId is not null)
        {
            return await GetByTraceIdAsync(job.TraceId, cancellationToken);
        }

        // Check flow step records
        var flowStep = await db.FlowStepRecords
            .Where(s => s.SpanId != null && s.SpanId.ToLower() == normalizedId)
            .FirstOrDefaultAsync(cancellationToken);

        if (flowStep?.TraceId is not null)
        {
            return await GetByTraceIdAsync(flowStep.TraceId, cancellationToken);
        }

        logger.LogInformation("TraceQuery by spanId={SpanId}: no matching record found", normalizedId);
        return null;
    }

    /// <summary>
    /// Parses a W3C traceparent string. Returns true when the string is a valid traceparent.
    /// </summary>
    public static bool TryParseTraceParent(string value, out string traceId, out string spanId)
    {
        var match = TraceParentRegex.Match(value.Trim());
        if (match.Success)
        {
            traceId = match.Groups[1].Value.ToLowerInvariant();
            spanId = match.Groups[2].Value.ToLowerInvariant();
            return true;
        }

        traceId = string.Empty;
        spanId = string.Empty;
        return false;
    }

    private static TraceModel? BuildTraceModel(
        string traceId,
        string? correlationId,
        List<JobStateRecord> jobs,
        List<ChainRunRecord> chainRuns,
        List<ServiceHealthRecord> healthRecords,
        List<FlowRunRecord> flowRuns,
        List<FlowStepRecord> flowSteps,
        List<SpanRecord> spanRecords)
    {
        if (jobs.Count == 0 && chainRuns.Count == 0 && healthRecords.Count == 0
            && flowRuns.Count == 0 && flowSteps.Count == 0 && spanRecords.Count == 0)
        {
            return null;
        }

        var spans = new List<SpanModel>();

        // Chain-run records become root spans
        foreach (var chainRun in chainRuns)
        {
            var duration = chainRun.CompletedAt.HasValue
                ? chainRun.CompletedAt.Value - chainRun.StartedAt
                : (TimeSpan?)null;

            var status = chainRun.Status switch
            {
                ChainRunStatus.Completed => SpanStatus.OK,
                ChainRunStatus.Failed => SpanStatus.Error,
                ChainRunStatus.Running => SpanStatus.InProgress,
                _ => SpanStatus.Unknown
            };

            spans.Add(new SpanModel
            {
                SpanId = chainRun.ChainRunId,
                ParentSpanId = null,
                ServiceName = "workerservice1",
                OperationName = "ChainTrigger.Run",
                StartTime = chainRun.StartedAt,
                Duration = duration,
                Status = status
            });
        }

        // Job-state records become child spans, parented to the chain run if one shares the correlation ID
        foreach (var job in jobs)
        {
            var status = job.Status switch
            {
                JobStatus.Completed => SpanStatus.OK,
                JobStatus.Failed => SpanStatus.Error,
                JobStatus.Processing => SpanStatus.InProgress,
                JobStatus.Queued => SpanStatus.InProgress,
                _ => SpanStatus.Unknown
            };

            var parentChainRun = chainRuns.FirstOrDefault(c => c.CorrelationId == job.CorrelationId);

            spans.Add(new SpanModel
            {
                SpanId = job.SpanId ?? job.JobId,
                ParentSpanId = parentChainRun?.ChainRunId,
                ServiceName = job.ServiceName,
                OperationName = "Worker.ProcessJob",
                StartTime = job.CreatedAt,
                Duration = job.Status is JobStatus.Completed or JobStatus.Failed
                    ? job.UpdatedAt - job.CreatedAt
                    : null,
                Status = status,
                ErrorMessage = job.ErrorMessage,
                LogEntries = [
                    new LogEntryModel
                    {
                        Timestamp = job.CreatedAt,
                        Level = "Information",
                        Message = $"Job {job.JobId} enqueued by {job.ServiceName}",
                        Attributes = new Dictionary<string, string>
                        {
                            ["job.id"] = job.JobId,
                            ["correlation.id"] = job.CorrelationId,
                        }
                    }
                ]
            });
        }

        // Service-health records appear as independent monitor spans
        foreach (var health in healthRecords)
        {
            spans.Add(new SpanModel
            {
                SpanId = health.Id.ToString(),
                ParentSpanId = null,
                ServiceName = health.CheckedByService,
                OperationName = $"StatusMonitor.Check.{health.ServiceName}",
                StartTime = health.CheckedAt,
                Duration = TimeSpan.Zero,
                Status = health.IsHealthy ? SpanStatus.OK : SpanStatus.Warning,
                HttpStatusCode = health.HttpStatusCode
            });
        }

        // FlowRun records become root spans; FlowStep records become their children
        foreach (var flowRun in flowRuns)
        {
            var flowRunDuration = flowRun.CompletedAt.HasValue
                ? flowRun.CompletedAt.Value - flowRun.StartedAt
                : (TimeSpan?)null;

            var flowRunStatus = flowRun.Status switch
            {
                FlowRunStatus.Completed => SpanStatus.OK,
                FlowRunStatus.Failed => SpanStatus.Error,
                FlowRunStatus.Running => SpanStatus.InProgress,
                _ => SpanStatus.Unknown
            };

            spans.Add(new SpanModel
            {
                SpanId = flowRun.FlowRunId,
                ParentSpanId = null,
                ServiceName = "AspireApp1.WorkerService1",
                OperationName = $"FlowRun.{flowRun.FlowName}",
                StartTime = flowRun.StartedAt,
                Duration = flowRunDuration,
                Status = flowRunStatus,
                ErrorMessage = flowRun.ErrorMessage,
                LogEntries = [
                    new LogEntryModel
                    {
                        Timestamp = flowRun.StartedAt,
                        Level = "Information",
                        Message = $"Flow '{flowRun.FlowName}' started. correlation.id={flowRun.CorrelationId}",
                        Attributes = new Dictionary<string, string>
                        {
                            ["flow.run.id"] = flowRun.FlowRunId,
                            ["correlation.id"] = flowRun.CorrelationId,
                        }
                    }
                ]
            });
        }

        // FlowStep records become child spans under their FlowRun
        foreach (var step in flowSteps)
        {
            var stepDuration = step.CompletedAt.HasValue && step.StartedAt.HasValue
                ? step.CompletedAt.Value - step.StartedAt.Value
                : (TimeSpan?)null;

            var stepStatus = step.Status switch
            {
                FlowStepStatus.Completed => SpanStatus.OK,
                FlowStepStatus.Failed => SpanStatus.Error,
                FlowStepStatus.Running => SpanStatus.InProgress,
                FlowStepStatus.Pending => SpanStatus.InProgress,
                _ => SpanStatus.Unknown
            };

            spans.Add(new SpanModel
            {
                SpanId = step.SpanId ?? $"{step.FlowRunId}_{step.StepOrder}",
                ParentSpanId = step.FlowRunId,
                ServiceName = step.ServiceName,
                OperationName = step.StepName,
                StartTime = step.StartedAt ?? DateTimeOffset.UtcNow,
                Duration = stepDuration,
                Status = stepStatus,
                ErrorMessage = step.ErrorMessage
            });
        }

        // SpanRecords from API services (e.g. ApiServiceForecast) with proper parent-child relationships
        foreach (var spanRec in spanRecords)
        {
            var spanDuration = spanRec.EndTime.HasValue
                ? spanRec.EndTime.Value - spanRec.StartTime
                : (TimeSpan?)null;

            var spanStatus = spanRec.Status switch
            {
                SpanRecordStatus.Error => SpanStatus.Error,
                SpanRecordStatus.Warning => SpanStatus.Warning,
                SpanRecordStatus.OK => SpanStatus.OK,
                _ => SpanStatus.Unknown
            };

            spans.Add(new SpanModel
            {
                SpanId = spanRec.SpanId,
                ParentSpanId = spanRec.ParentSpanId,
                ServiceName = spanRec.ServiceName,
                OperationName = spanRec.OperationName,
                StartTime = spanRec.StartTime,
                Duration = spanDuration,
                Status = spanStatus,
                ErrorMessage = spanRec.ErrorMessage,
                HttpStatusCode = spanRec.HttpStatusCode
            });
        }

        // Derive overall trace status
        var overallStatus = spans.Any(s => s.Status == SpanStatus.Error)
            ? SpanStatus.Error
            : spans.Any(s => s.Status == SpanStatus.InProgress)
                ? SpanStatus.InProgress
                : spans.Any(s => s.Status == SpanStatus.Warning)
                    ? SpanStatus.Warning
                    : spans.Count > 0 ? SpanStatus.OK : SpanStatus.Unknown;

        return new TraceModel
        {
            TraceId = traceId,
            CorrelationId = correlationId
                ?? jobs.FirstOrDefault()?.CorrelationId
                ?? chainRuns.FirstOrDefault()?.CorrelationId
                ?? flowRuns.FirstOrDefault()?.CorrelationId,
            OverallStatus = overallStatus,
            Spans = spans,
            StartTime = spans.Count > 0 ? spans.Min(s => s.StartTime) : DateTimeOffset.UtcNow
        };
    }
}
