namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Hands submission ids from the web requests that create or re-run them to the
/// background worker that analyses them, so no HTTP request ever waits on an
/// analysis.
///
/// The queue is only a wake-up signal. The database's AnalysisStatus is the
/// source of truth: the worker re-reads it before doing anything, so a duplicate
/// or stale id is harmless, and anything queued when the app stopped is found
/// again from the database at the next start.
/// </summary>
public interface IAnalysisQueue
{
    ValueTask EnqueueAsync(int submissionId, CancellationToken cancellationToken = default);

    /// <summary>Yields ids as they arrive, until <paramref name="cancellationToken"/> fires.</summary>
    IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken);
}
