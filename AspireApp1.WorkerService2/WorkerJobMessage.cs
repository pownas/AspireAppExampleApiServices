namespace AspireApp1.WorkerService1;

public sealed record WorkerJobMessage(
    string JobId,
    string TraceParent,
    string? TraceState,
    string CorrelationId);
