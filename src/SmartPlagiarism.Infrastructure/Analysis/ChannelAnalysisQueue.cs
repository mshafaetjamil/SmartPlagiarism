using System.Threading.Channels;
using SmartPlagiarism.Core.Abstractions;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>
/// <see cref="IAnalysisQueue"/> over an in-memory channel.
///
/// Unbounded: an enqueue is only an int, and a web request must never block
/// because the worker is busy. Losing the contents on restart is fine - the
/// database still says Queued, and the worker re-reads it at startup.
/// </summary>
public sealed class ChannelAnalysisQueue : IAnalysisQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        // One worker drains the queue, so submissions are analysed in order and
        // each sees the fingerprints of every one before it.
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(int submissionId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(submissionId, cancellationToken);

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
