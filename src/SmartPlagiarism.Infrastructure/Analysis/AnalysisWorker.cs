using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>
/// Drains the analysis queue, one submission at a time, each in its own DI
/// scope so every analysis gets a fresh DbContext.
///
/// Nothing that goes wrong with one submission may stop the loop: the analyzer
/// records its own failures, and anything that still escapes - a database that
/// has gone away, say - is logged here and the loop moves on. An unhandled
/// exception would otherwise stop the whole web host.
/// </summary>
public sealed class AnalysisWorker : BackgroundService
{
    private readonly IAnalysisQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalysisWorker> _logger;

    public AnalysisWorker(IAnalysisQueue queue, IServiceScopeFactory scopeFactory, ILogger<AnalysisWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The backlog goes first, before anything submitted since startup, so
            // analysis order stays submission order: a new submission copying an
            // old one still waiting must find the old one's fingerprints.
            foreach (var submissionId in await FindPendingAsync(stoppingToken))
            {
                await AnalyzeAsync(submissionId, stoppingToken);
            }

            // Anything also queued during startup was handled above and is now
            // skipped by the analyzer, as it is no longer Queued.
            await foreach (var submissionId in _queue.DequeueAllAsync(stoppingToken))
            {
                await AnalyzeAsync(submissionId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown. Whatever was in flight is resumed at the next start.
        }
    }

    /// <summary>
    /// What the database says is waiting - submissions queued before a restart,
    /// and ones a crash left Processing - since the in-memory queue starts empty.
    /// </summary>
    private async Task<IReadOnlyList<int>> FindPendingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var analyzer = scope.ServiceProvider.GetRequiredService<ISubmissionAnalyzer>();

            var pending = await analyzer.ResumePendingAsync(stoppingToken);

            if (pending.Count > 0)
            {
                _logger.LogInformation("Analysing {Count} submission(s) waiting from before startup.", pending.Count);
            }

            return pending;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            // Most likely the database is not reachable yet. New submissions are
            // still processed as they arrive; older ones wait for the next start.
            _logger.LogError(ex, "Could not find pending analyses at startup.");
            return [];
        }
    }

    private async Task AnalyzeAsync(int submissionId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var analyzer = scope.ServiceProvider.GetRequiredService<ISubmissionAnalyzer>();

            await analyzer.AnalyzeAsync(submissionId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The analyzer could not even record the failure. The submission is
            // left as it was and is picked up again at the next start.
            _logger.LogError(ex, "Analysis of submission {SubmissionId} could not be completed.", submissionId);
        }
    }
}
