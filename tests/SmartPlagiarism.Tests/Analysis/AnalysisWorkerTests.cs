using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Infrastructure.Analysis;

namespace SmartPlagiarism.Tests.Analysis;

/// <summary>
/// The worker loop itself: it resumes what was pending at startup, processes the
/// queue in order, and keeps going when one submission blows up.
/// </summary>
public class AnalysisWorkerTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Worker_ResumesPendingWork_ThenDrainsTheQueue_SurvivingAFailure()
    {
        var analyzer = new ScriptedAnalyzer(pendingAtStartup: [7], failFor: 1);
        var queue = new ChannelAnalysisQueue();

        await using var provider = new ServiceCollection()
            .AddSingleton<ISubmissionAnalyzer>(analyzer)
            .BuildServiceProvider();

        using var worker = new AnalysisWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnalysisWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        await queue.EnqueueAsync(1);
        await queue.EnqueueAsync(2);

        await analyzer.WaitForAsync(2, s_timeout);
        await worker.StopAsync(CancellationToken.None);

        analyzer.Analyzed.Should().Equal([7, 1, 2], "startup work first, then the queue in order, past the failure");
    }

    [Fact]
    public async Task Worker_WhenResumeFails_StillProcessesNewSubmissions()
    {
        var analyzer = new ScriptedAnalyzer(pendingAtStartup: [], failFor: null, failResume: true);
        var queue = new ChannelAnalysisQueue();

        await using var provider = new ServiceCollection()
            .AddSingleton<ISubmissionAnalyzer>(analyzer)
            .BuildServiceProvider();

        using var worker = new AnalysisWorker(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnalysisWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(3);

        await analyzer.WaitForAsync(3, s_timeout);
        await worker.StopAsync(CancellationToken.None);

        analyzer.Analyzed.Should().Equal(3);
    }

    private sealed class ScriptedAnalyzer(IReadOnlyList<int> pendingAtStartup, int? failFor, bool failResume = false)
        : ISubmissionAnalyzer
    {
        private readonly ConcurrentQueue<int> _analyzed = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _waiters = new();

        public IReadOnlyList<int> Analyzed => [.. _analyzed];

        public Task AnalyzeAsync(int submissionId, CancellationToken cancellationToken = default)
        {
            _analyzed.Enqueue(submissionId);
            _waiters.GetOrAdd(submissionId, _ => new TaskCompletionSource()).TrySetResult();

            return submissionId == failFor
                ? throw new InvalidOperationException("The database went away.")
                : Task.CompletedTask;
        }

        public Task<IReadOnlyList<int>> ResumePendingAsync(CancellationToken cancellationToken = default) =>
            failResume
                ? throw new InvalidOperationException("The database is not up yet.")
                : Task.FromResult(pendingAtStartup);

        public Task WaitForAsync(int submissionId, TimeSpan timeout) =>
            _waiters.GetOrAdd(submissionId, _ => new TaskCompletionSource()).Task.WaitAsync(timeout);
    }
}
