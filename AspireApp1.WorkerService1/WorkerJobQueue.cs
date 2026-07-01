using System.Threading.Channels;

namespace AspireApp1.WorkerService1;

public sealed class WorkerJobQueue
{
    private readonly Channel<WorkerJobMessage> queue = Channel.CreateUnbounded<WorkerJobMessage>();

    public ValueTask EnqueueAsync(WorkerJobMessage message, CancellationToken cancellationToken)
    {
        return queue.Writer.WriteAsync(message, cancellationToken);
    }

    public IAsyncEnumerable<WorkerJobMessage> DequeueAllAsync(CancellationToken cancellationToken)
    {
        return queue.Reader.ReadAllAsync(cancellationToken);
    }
}
