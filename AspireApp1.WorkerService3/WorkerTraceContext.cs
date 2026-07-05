using System.Diagnostics;

namespace AspireApp1.WorkerService1;

public static class WorkerTraceContext
{
    public static bool TryParse(string? traceParent, string? traceState, out ActivityContext parentContext)
    {
        if (string.IsNullOrWhiteSpace(traceParent))
        {
            parentContext = default;
            return false;
        }

        return ActivityContext.TryParse(traceParent, traceState, out parentContext);
    }
}
