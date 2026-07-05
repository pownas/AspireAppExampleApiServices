namespace AspireApp1.WorkerService2;

public sealed record WorkerJobMessage(
    string JobId,
    string TraceParent,
    string? TraceState,
    string CorrelationId);
