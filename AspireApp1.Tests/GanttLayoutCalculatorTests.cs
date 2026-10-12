using AspireApp1.Web.Models;

namespace AspireApp1.Tests;

[TestClass]
public class GanttLayoutCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void BuildRows_PlacesChildrenDirectlyAfterParentWithDepth()
    {
        List<SpanModel> spans =
        [
            Span("child-late", "root", 50, 10),
            Span("root", null, 0, 100),
            Span("child-early", "root", 10, 20),
            Span("grandchild", "child-early", 12, 5)
        ];

        var rows = GanttLayoutCalculator.BuildRows(spans);

        CollectionAssert.AreEqual(
            new[] { "root", "child-early", "grandchild", "child-late" },
            rows.Select(r => r.Span.SpanId).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 1 }, rows.Select(r => r.Depth).ToArray());
    }

    [TestMethod]
    public void BuildRows_MarksChainThatEndsLastAsCriticalPath()
    {
        List<SpanModel> spans =
        [
            Span("root", null, 0, 100),
            Span("before", "root", 2, 10),
            Span("parallel", "root", 25, 10),
            Span("slow", "root", 20, 75),
            Span("slow-child", "slow", 30, 60)
        ];

        var critical = GanttLayoutCalculator.BuildRows(spans)
            .Where(r => r.IsOnCriticalPath)
            .Select(r => r.Span.SpanId)
            .ToArray();

        // "parallel" overlaps "slow" so it does not delay the trace; "before" finished before "slow" started.
        CollectionAssert.AreEquivalent(new[] { "root", "slow", "slow-child", "before" }, critical);
    }

    [TestMethod]
    public void BuildRows_SequentialRootSpans_AreAllOnCriticalPath()
    {
        List<SpanModel> spans =
        [
            Span("call-a", "missing-http-span", 0, 250),
            Span("call-b", "missing-http-span", 255, 240),
            Span("queue", "missing-http-span", 500, 20),
            Span("worker", "other-missing", 530, 60)
        ];

        var rows = GanttLayoutCalculator.BuildRows(spans);

        Assert.IsTrue(rows.All(r => r.IsOnCriticalPath));
    }

    [TestMethod]
    public void BuildRows_SpanWithUnknownParentBecomesRoot()
    {
        var rows = GanttLayoutCalculator.BuildRows([Span("orphan", "missing", 0, 10)]);

        Assert.AreEqual(0, rows.Single().Depth);
    }

    [TestMethod]
    public void BuildTicks_UsesNiceStepsFromZero()
    {
        var ticks = GanttLayoutCalculator.BuildTicks(1000);

        Assert.AreEqual("0 ms", ticks[0].Label);
        Assert.AreEqual(0d, ticks[0].OffsetPercent);
        Assert.AreEqual("1 s", ticks[^1].Label);
        Assert.AreEqual(100d, ticks[^1].OffsetPercent, 0.001);
        Assert.IsTrue(ticks.Count <= 9);
    }

    [TestMethod]
    public void FormatMs_UsesSwedishDecimalComma()
    {
        Assert.AreEqual("250 ms", GanttLayoutCalculator.FormatMs(250));
        Assert.AreEqual("1,5 s", GanttLayoutCalculator.FormatMs(1500));
    }

    private static SpanModel Span(string id, string? parent, int startMs, int durationMs) => new()
    {
        SpanId = id,
        ParentSpanId = parent,
        ServiceName = "svc",
        OperationName = id,
        StartTime = T0.AddMilliseconds(startMs),
        Duration = TimeSpan.FromMilliseconds(durationMs),
        Status = SpanStatus.OK
    };
}
