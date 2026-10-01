using AspireApp1.StateStore;
using Microsoft.EntityFrameworkCore;

namespace AspireApp1.Web.Insights;

/// <summary>
/// Loads recent state-store records and aggregates them with <see cref="InsightsCalculator"/>.
/// </summary>
/// <remarks>
/// Rows are fetched newest-first by primary key with an upper bound and filtered by time in memory.
/// This keeps the queries portable: SQLite cannot compare or order <see cref="DateTimeOffset"/> columns in SQL.
/// </remarks>
public class InsightsQueryService(IDbContextFactory<StateStoreDbContext> dbFactory, TimeProvider timeProvider)
{
    /// <summary>Maximum number of rows read per table for one snapshot.</summary>
    internal const int MaxRowsPerTable = 20_000;

    /// <summary>
    /// Builds all insights for the given time window ending now.
    /// </summary>
    /// <param name="window">How far back to look.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A snapshot with every aggregated view.</returns>
    public async Task<InsightsSnapshot> GetSnapshotAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        var to = timeProvider.GetUtcNow();
        var from = to - window;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var spans = (await LatestAsync(db.SpanRecords, cancellationToken))
            .Where(s => s.StartTime >= from && s.StartTime < to)
            .ToList();
        var runs = (await LatestAsync(db.FlowRunRecords, cancellationToken))
            .Where(r => r.StartedAt >= from && r.StartedAt < to)
            .ToList();
        // Steps are matched to runs in the window rather than by their own timestamps:
        // pending steps have no timestamps but still tell where a run stopped.
        var runIds = runs.Select(r => r.FlowRunId).ToHashSet(StringComparer.Ordinal);
        var steps = (await LatestAsync(db.FlowStepRecords, cancellationToken))
            .Where(s => runIds.Contains(s.FlowRunId))
            .ToList();
        var health = (await LatestAsync(db.ServiceHealthRecords, cancellationToken))
            .Where(h => h.CheckedAt >= from && h.CheckedAt < to)
            .ToList();
        var jobs = (await LatestAsync(db.JobStates, cancellationToken))
            .Where(j => j.CreatedAt >= from && j.CreatedAt < to)
            .ToList();
        var chainRuns = (await LatestAsync(db.ChainRunRecords, cancellationToken))
            .Where(c => c.StartedAt >= from && c.StartedAt < to)
            .ToList();

        return new InsightsSnapshot
        {
            From = from,
            To = to,
            FailureHotspots = InsightsCalculator.CalculateFailureHotspots(runs, steps),
            HealthTimeline = InsightsCalculator.CalculateHealthTimeline(health, from, to),
            DependencyGraph = InsightsCalculator.CalculateDependencyGraph(spans, steps),
            FlowTrend = InsightsCalculator.CalculateFlowTrend(runs, from, to),
            ServiceLatencies = InsightsCalculator.CalculateServiceLatencies(spans),
            SlowestOperations = InsightsCalculator.CalculateSlowestOperations(spans),
            RetryStats = InsightsCalculator.CalculateRetryStats(steps),
            JobStatuses = InsightsCalculator.CalculateJobStatuses(jobs),
            ChainRuns = InsightsCalculator.CalculateChainRuns(chainRuns)
        };
    }

    /// <summary>
    /// Reads the newest rows of a table by primary key, capped at <see cref="MaxRowsPerTable"/>.
    /// </summary>
    /// <typeparam name="T">State-store entity with an integer <c>Id</c> key.</typeparam>
    /// <param name="query">The table to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Newest rows first.</returns>
    private static Task<List<T>> LatestAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        where T : class =>
        query.AsNoTracking()
            .OrderByDescending(r => EF.Property<int>(r, "Id"))
            .Take(MaxRowsPerTable)
            .ToListAsync(cancellationToken);
}
