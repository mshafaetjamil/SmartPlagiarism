using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>
/// A background worker that drains a queue of record ids, each in its own DI
/// scope so every unit of work gets a fresh DbContext.
///
/// Work waiting in the database from before a restart is done first, before
/// anything queued since, so work starts in the order it arrived. With a
/// <see cref="Concurrency"/> above one, that many items are worked on at once, so
/// one slow item - a long scan going through OCR - does not hold up everything
/// queued behind it. Nothing that goes wrong with one item may stop the loop:
/// an exception that escapes is logged and the loop moves on, since an
/// unhandled one would stop the whole web host.
/// </summary>
public abstract class QueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;

    // Ids being worked on. The queue can hold one id twice - queued by a request
    // while the startup scan also found it - and two readers must not take both.
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();

    protected QueueWorker(IServiceScopeFactory scopeFactory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>What the work is called in log messages, e.g. "analysis".</summary>
    protected abstract string WorkName { get; }

    protected abstract IAsyncEnumerable<int> DequeueAllAsync(CancellationToken stoppingToken);

    /// <summary>Ids the database says are waiting, oldest first.</summary>
    protected abstract Task<IReadOnlyList<int>> FindPendingAsync(IServiceProvider services, CancellationToken stoppingToken);

    protected abstract Task ProcessAsync(IServiceProvider services, int id, CancellationToken stoppingToken);

    /// <summary>Items worked on at once.</summary>
    protected virtual int Concurrency => 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Parallel.ForEachAsync(
                await FindPendingSafelyAsync(stoppingToken),
                new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = stoppingToken },
                async (id, cancellationToken) => await ProcessOnceAsync(id, cancellationToken));

            // Anything also queued during startup was handled above and is now
            // skipped, as its status is no longer "waiting".
            await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => DrainAsync(stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown. Whatever was in flight is resumed at the next start.
        }
    }

    private async Task DrainAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in DequeueAllAsync(stoppingToken))
        {
            await ProcessOnceAsync(id, stoppingToken);
        }
    }

    private async Task ProcessOnceAsync(int id, CancellationToken stoppingToken)
    {
        if (!_inFlight.TryAdd(id, 0))
        {
            return;
        }

        try
        {
            await ProcessSafelyAsync(id, stoppingToken);
        }
        finally
        {
            _inFlight.TryRemove(id, out _);
        }
    }

    private async Task<IReadOnlyList<int>> FindPendingSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var pending = await FindPendingAsync(scope.ServiceProvider, stoppingToken);

            if (pending.Count > 0)
            {
                _logger.LogInformation("Resuming {Count} {Work} item(s) waiting from before startup.", pending.Count, WorkName);
            }

            return pending;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Most likely the database is not reachable yet. New work is still
            // processed as it arrives; older work waits for the next start.
            _logger.LogError(ex, "Could not find pending {Work} work at startup.", WorkName);
            return [];
        }
    }

    private async Task ProcessSafelyAsync(int id, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await ProcessAsync(scope.ServiceProvider, id, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The handler could not even record the failure. The item is left as
            // it was and is picked up again at the next start.
            _logger.LogError(ex, "{Work} of item {Id} could not be completed.", WorkName, id);
        }
    }
}

/// <summary>Drains the analysis queue: one submission at a time, oldest first.</summary>
public sealed class AnalysisWorker(
    IAnalysisQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AnalysisWorker> logger,
    IOptions<AnalysisOptions>? options = null)
    : QueueWorker(scopeFactory, logger)
{
    protected override string WorkName => "analysis";

    protected override int Concurrency { get; } =
        Math.Clamp(options?.Value.Workers ?? 1, 1, AnalysisOptions.MaxWorkers);

    protected override IAsyncEnumerable<int> DequeueAllAsync(CancellationToken stoppingToken) =>
        queue.DequeueAllAsync(stoppingToken);

    protected override Task<IReadOnlyList<int>> FindPendingAsync(IServiceProvider services, CancellationToken stoppingToken) =>
        services.GetRequiredService<ISubmissionAnalyzer>().ResumePendingAsync(stoppingToken);

    protected override Task ProcessAsync(IServiceProvider services, int id, CancellationToken stoppingToken) =>
        services.GetRequiredService<ISubmissionAnalyzer>().AnalyzeAsync(id, stoppingToken);
}

/// <summary>
/// Drains the archive import queue. Separate from analysis, so a ZIP of
/// thousands of old reports never holds up a student's result.
/// </summary>
public sealed class ArchiveImportWorker(IArchiveImportQueue queue, IServiceScopeFactory scopeFactory, ILogger<ArchiveImportWorker> logger)
    : QueueWorker(scopeFactory, logger)
{
    protected override string WorkName => "archive import";

    protected override IAsyncEnumerable<int> DequeueAllAsync(CancellationToken stoppingToken) =>
        queue.DequeueAllAsync(stoppingToken);

    protected override Task<IReadOnlyList<int>> FindPendingAsync(IServiceProvider services, CancellationToken stoppingToken) =>
        services.GetRequiredService<IArchiveImporter>().ResumePendingAsync(stoppingToken);

    protected override Task ProcessAsync(IServiceProvider services, int id, CancellationToken stoppingToken) =>
        services.GetRequiredService<IArchiveImporter>().ImportAsync(id, stoppingToken);
}
