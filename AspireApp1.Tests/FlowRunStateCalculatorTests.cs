using AspireApp1.StateStore;
using AspireApp1.Web.Models;

namespace AspireApp1.Tests;

[TestClass]
public class FlowRunStateCalculatorTests
{
    [TestMethod]
    public void Build_FailedRunWithoutFailedStep_ReportsErrorAtFirstUnfinishedStep()
    {
        var run = new FlowRunRecord
        {
            FlowRunId = "run",
            FlowName = "RetryDemoFlow",
            CorrelationId = "c",
            StartedAt = DateTimeOffset.UtcNow,
            Status = FlowRunStatus.Failed,
            ErrorMessage = "The operation didn't complete within the allowed timeout of '00:00:30'."
        };
        FlowStepRecord[] steps =
        [
            new() { FlowRunId = "run", StepOrder = 1, StepName = "Step1", ServiceName = "ws1", Status = FlowStepStatus.Completed },
            new() { FlowRunId = "run", StepOrder = 2, StepName = "Step2", ServiceName = "ws2", Status = FlowStepStatus.Completed },
            new() { FlowRunId = "run", StepOrder = 3, StepName = "Step3", ServiceName = "ws3", Status = FlowStepStatus.Pending }
        ];

        var state = FlowRunStateCalculator.Build(run, steps);

        Assert.AreEqual("Fel", state.Status);
        Assert.AreEqual(3, state.ErrorStep);
        Assert.AreEqual("ws3", state.CurrentService);
        StringAssert.Contains(state.ErrorMessage, "timeout");
    }
}
