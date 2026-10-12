using AspireApp1.StateStore;

namespace AspireApp1.Web.Models;

public static class FlowRunStateCalculator
{
    public static FlowRunStateModel Build(FlowRunRecord flowRun, IEnumerable<FlowStepRecord> flowSteps)
    {
        var runSteps = flowSteps
            .Where(s => s.FlowRunId == flowRun.FlowRunId)
            .OrderBy(s => s.StepOrder)
            .ToList();

        var totalSteps = runSteps.Count > 0 ? runSteps.Max(s => s.StepOrder) : 0;
        // A run can fail (e.g. time out) without any step being marked Failed; it then stopped at the first unfinished step.
        var firstFailed = runSteps.FirstOrDefault(s => s.Status == FlowStepStatus.Failed)
            ?? (flowRun.Status == FlowRunStatus.Failed ? runSteps.FirstOrDefault(s => s.Status != FlowStepStatus.Completed) : null);
        var activeStep = runSteps.FirstOrDefault(s => s.Status is FlowStepStatus.Running or FlowStepStatus.Retrying);
        var lastSuccess = runSteps
            .Where(s => s.Status == FlowStepStatus.Completed)
            .Select(s => (int?)s.StepOrder)
            .Max();

        var currentStep = firstFailed?.StepOrder
            ?? activeStep?.StepOrder
            ?? lastSuccess
            ?? (totalSteps > 0 ? 1 : 0);
        var nextPendingStep = runSteps.FirstOrDefault(s => s.Status == FlowStepStatus.Pending);

        var currentService = firstFailed?.ServiceName
            ?? activeStep?.ServiceName
            ?? nextPendingStep?.ServiceName
            ?? runSteps.FirstOrDefault(s => s.StepOrder == currentStep)?.ServiceName;

        var status = firstFailed is not null
            ? "Fel"
            : activeStep is not null
                ? "Pågår"
                : runSteps.Any(s => s.Status == FlowStepStatus.Pending) && !runSteps.Any(s => s.Status == FlowStepStatus.Completed)
                    ? "Väntar"
                    : flowRun.Status == FlowRunStatus.Running
                        ? "Pågår"
                        : flowRun.Status == FlowRunStatus.Completed
                            ? "Klar"
                            : flowRun.Status == FlowRunStatus.Failed
                                ? "Fel"
                                : "Okänd";

        return new FlowRunStateModel
        {
            FlowRunId = flowRun.FlowRunId,
            FlowName = flowRun.FlowName,
            CurrentStep = currentStep,
            TotalSteps = totalSteps,
            CurrentService = currentService,
            Status = status,
            LastSuccessStep = lastSuccess,
            ErrorStep = firstFailed?.StepOrder,
            ErrorMessage = firstFailed?.ErrorMessage ?? flowRun.ErrorMessage
        };
    }
}
