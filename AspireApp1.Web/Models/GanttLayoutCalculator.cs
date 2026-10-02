namespace AspireApp1.Web.Models;

/// <summary>One row in the timeline (Gantt) view.</summary>
/// <param name="Span">The span shown on the row.</param>
/// <param name="Depth">Nesting depth (0 = root span).</param>
/// <param name="IsOnCriticalPath">True if the span is part of the chain that determines the total duration.</param>
public sealed record GanttRow(SpanModel Span, int Depth, bool IsOnCriticalPath);

/// <summary>A labelled tick on the timeline axis.</summary>
/// <param name="OffsetPercent">Horizontal position, 0-100.</param>
/// <param name="Label">Display text, e.g. "0,5 s".</param>
public sealed record GanttTick(double OffsetPercent, string Label);

/// <summary>
/// Lays out spans for the timeline view: parent/child order, indentation depth,
/// critical path and axis ticks.
/// </summary>
public static class GanttLayoutCalculator
{
    /// <summary>
    /// Orders spans depth-first (each parent directly followed by its children, siblings by start time)
    /// and marks the critical path.
    /// </summary>
    /// <param name="spans">Spans to lay out; parents missing from the list make a span a root.</param>
    /// <returns>Rows in display order.</returns>
    /// <remarks>
    /// The critical path is the chain of spans that decides when the trace finishes: the span that ends last,
    /// the sibling it had to wait for (ended last before it started), and so on, recursively into children.
    /// </remarks>
    public static IReadOnlyList<GanttRow> BuildRows(IReadOnlyList<SpanModel> spans)
    {
        var ids = new HashSet<string>(spans.Select(s => s.SpanId), StringComparer.OrdinalIgnoreCase);
        var childrenByParent = spans
            .Where(s => s.ParentSpanId is not null && ids.Contains(s.ParentSpanId))
            .GroupBy(s => s.ParentSpanId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StartTime).ToList(), StringComparer.OrdinalIgnoreCase);
        var roots = spans
            .Where(s => s.ParentSpanId is null || !ids.Contains(s.ParentSpanId))
            .OrderBy(s => s.StartTime)
            .ToList();

        var critical = FindCriticalPath(roots, childrenByParent);
        var rows = new List<GanttRow>(spans.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(SpanModel span, int depth)
        {
            if (!visited.Add(span.SpanId))
            {
                return;
            }

            rows.Add(new GanttRow(span, depth, critical.Contains(span.SpanId)));
            foreach (var child in childrenByParent.GetValueOrDefault(span.SpanId) ?? [])
            {
                Visit(child, depth + 1);
            }
        }

        foreach (var root in roots)
        {
            Visit(root, 0);
        }

        return rows;
    }

    /// <summary>
    /// Creates evenly spaced axis ticks with a "nice" step (1, 2 or 5 × 10^n milliseconds).
    /// </summary>
    /// <param name="totalMs">Total timeline length in milliseconds.</param>
    /// <param name="maxTicks">Upper bound on the number of intervals.</param>
    /// <returns>Ticks from 0 up to (at most) <paramref name="totalMs"/>.</returns>
    public static IReadOnlyList<GanttTick> BuildTicks(double totalMs, int maxTicks = 8)
    {
        if (totalMs <= 0)
        {
            return [new GanttTick(0, FormatMs(0))];
        }

        var rawStep = totalMs / maxTicks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = new[] { 1d, 2d, 5d, 10d }.Select(m => m * magnitude).First(s => s >= rawStep);

        var ticks = new List<GanttTick>();
        for (var value = 0d; value <= totalMs + step * 1e-9; value += step)
        {
            ticks.Add(new GanttTick(value / totalMs * 100, FormatMs(value)));
        }

        return ticks;
    }

    /// <summary>
    /// Formats a millisecond value for the axis ("250 ms", "1,5 s").
    /// </summary>
    /// <param name="ms">Milliseconds.</param>
    /// <returns>Short display text using Swedish number formatting.</returns>
    public static string FormatMs(double ms)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("sv-SE");
        return ms < 1000
            ? $"{ms.ToString("0", culture)} ms"
            : $"{(ms / 1000).ToString("0.##", culture)} s";
    }

    private static HashSet<string> FindCriticalPath(
        IReadOnlyList<SpanModel> roots, IReadOnlyDictionary<string, List<SpanModel>> childrenByParent)
    {
        var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MarkCriticalChain(roots, childrenByParent, path);
        return path;
    }

    /// <summary>
    /// Walks backwards through a set of sibling spans: starts at the one that ends last, then repeatedly
    /// jumps to the sibling that finished last before the current one started (the one it waited for).
    /// Each span on that chain is marked and its own children are processed the same way.
    /// </summary>
    /// <param name="siblings">Spans sharing the same parent (or the roots).</param>
    /// <param name="childrenByParent">Children lookup.</param>
    /// <param name="path">Collects the span ids on the critical path.</param>
    private static void MarkCriticalChain(
        IReadOnlyList<SpanModel> siblings, IReadOnlyDictionary<string, List<SpanModel>> childrenByParent, HashSet<string> path)
    {
        var current = siblings.OrderByDescending(End).FirstOrDefault();
        while (current is not null && path.Add(current.SpanId))
        {
            MarkCriticalChain(childrenByParent.GetValueOrDefault(current.SpanId) ?? [], childrenByParent, path);
            var start = current.StartTime;
            current = siblings
                .Where(s => End(s) <= start && !path.Contains(s.SpanId))
                .OrderByDescending(End)
                .FirstOrDefault();
        }
    }

    private static DateTimeOffset End(SpanModel span) => span.StartTime + (span.Duration ?? TimeSpan.Zero);
}
