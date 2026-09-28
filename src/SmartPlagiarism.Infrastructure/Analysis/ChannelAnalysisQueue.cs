using System.Threading.Channels;
using SmartPlagiarism.Core.Abstractions;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>
/// An in-memory queue of record ids for one background worker.
///
/// Unbounded: an enqueue is only an int, and a web request must never block
/// because the worker is busy. Losing the contents on restart is fine - the
/// database still says what is waiting, and the worker re-reads it at startup.
/// </summary>
public abstract class ChannelWorkQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        // A worker may read with several loops at once (QueueWorker.Concurrency);
        // items still leave in the order they arrived.
        SingleReader = false,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(int id, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(id, cancellationToken);

    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Submissions waiting for analysis. One reader means submissions are analysed
/// in order, so each sees the fingerprints of every one before it.
/// </summary>
public sealed class ChannelAnalysisQueue : ChannelWorkQueue, IAnalysisQueue;

/// <summary>Archive imports waiting to run, one at a time in upload order.</summary>
public sealed class ChannelArchiveImportQueue : ChannelWorkQueue, IArchiveImportQueue;
