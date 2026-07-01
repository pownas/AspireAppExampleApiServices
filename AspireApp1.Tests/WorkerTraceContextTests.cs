using System.Diagnostics;
using AspireApp1.WorkerService1;

namespace AspireApp1.Tests;

[TestClass]
public class WorkerTraceContextTests
{
    [TestMethod]
    public void TryParse_WithValidTraceParent_ReturnsTrue()
    {
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var result = WorkerTraceContext.TryParse(activity.Id, activity.TraceStateString, out var context);

        Assert.IsTrue(result);
        Assert.AreEqual(activity.TraceId, context.TraceId);
    }

    [TestMethod]
    public void TryParse_WithMissingTraceParent_ReturnsFalse()
    {
        var result = WorkerTraceContext.TryParse(null, null, out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryParse_WithInvalidTraceParent_ReturnsFalse()
    {
        var result = WorkerTraceContext.TryParse("invalid-traceparent", null, out _);

        Assert.IsFalse(result);
    }
}
