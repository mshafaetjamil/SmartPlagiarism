using FluentAssertions;
using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Tests.Ocr;

/// <summary>
/// The time limit on in-process OCR, with a stand-in for the Tesseract engine: a
/// call that overruns is given up on, its engine retired until the call returns,
/// and the next call carries on with a fresh one - never with more than one
/// engine retired at a time.
/// </summary>
public sealed class TimeLimitedEngineTests : IDisposable
{
    private static readonly TimeSpan s_timeLimit = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_generous = TimeSpan.FromSeconds(10);

    private readonly List<FakeEngine> _engines = [];

    // Released when each test ends, so no call is left blocking a thread.
    private readonly List<ManualResetEventSlim> _stuckCalls = [];

    [Fact]
    public async Task CallsWithinTheLimit_ShareOneEngine()
    {
        using var engines = NewEngines();

        (await engines.RunAsync(engine => engine)).Should().BeSameAs(_engines[0]);
        (await engines.RunAsync(engine => engine)).Should().BeSameAs(_engines[0]);
        _engines.Should().ContainSingle();
    }

    [Fact]
    public async Task ACallThatFails_PassesItsErrorOn_AndKeepsTheEngine()
    {
        using var engines = NewEngines();

        await FluentActions.Awaiting(() => engines.RunAsync<int>(_ => throw new InvalidOperationException("unreadable image")))
            .Should().ThrowAsync<InvalidOperationException>();

        (await engines.RunAsync(engine => engine)).Should().BeSameAs(_engines[0]);
    }

    [Fact]
    public async Task ACallThatOverruns_TimesOut_AndTheNextCallGetsAFreshEngine()
    {
        using var engines = NewEngines();
        var stuck = StuckCall();

        await FluentActions.Awaiting(() => engines.RunAsync(engine => Block(stuck, engine)))
            .Should().ThrowAsync<TimeoutException>();

        var next = await engines.RunAsync(engine => engine).WaitAsync(s_generous);

        next.Should().NotBeSameAs(_engines[0]);
        _engines[0].IsDisposed.Should().BeFalse("the stuck call is still using it");

        stuck.Set();
        await _engines[0].Disposed.WaitAsync(s_generous);
    }

    [Fact]
    public async Task ASecondOverrunWhileAnEngineIsRetired_KeepsItsEngine_AndTheNextCallWaitsForIt()
    {
        using var engines = NewEngines();
        var firstStuck = StuckCall();
        var secondStuck = StuckCall();

        await FluentActions.Awaiting(() => engines.RunAsync(engine => Block(firstStuck, engine)))
            .Should().ThrowAsync<TimeoutException>();
        await FluentActions.Awaiting(() => engines.RunAsync(engine => Block(secondStuck, engine)))
            .Should().ThrowAsync<TimeoutException>();

        var third = engines.RunAsync(engine => engine);
        await Task.Delay(s_timeLimit * 3);

        third.IsCompleted.Should().BeFalse("the second stuck call still holds its engine");
        _engines.Should().HaveCount(2, "only the first stuck engine was replaced");

        secondStuck.Set();
        (await third.WaitAsync(s_generous)).Should().BeSameAs(_engines[1]);
    }

    [Fact]
    public void Disposing_FreesAnIdleEngine()
    {
        var engines = NewEngines();

        engines.Dispose();

        _engines[0].IsDisposed.Should().BeTrue();
    }

    public void Dispose()
    {
        foreach (var call in _stuckCalls)
        {
            call.Set();
        }
    }

    private TimeLimitedEngine<FakeEngine> NewEngines()
    {
        var first = new FakeEngine();
        _engines.Add(first);

        return new TimeLimitedEngine<FakeEngine>(
            first,
            () =>
            {
                var replacement = new FakeEngine();
                _engines.Add(replacement);
                return replacement;
            },
            s_timeLimit);
    }

    private ManualResetEventSlim StuckCall()
    {
        var call = new ManualResetEventSlim();
        _stuckCalls.Add(call);
        return call;
    }

    /// <summary>A native call that will not return until released.</summary>
    private static FakeEngine Block(ManualResetEventSlim until, FakeEngine engine)
    {
        until.Wait();
        return engine;
    }

    private sealed class FakeEngine : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public bool IsDisposed => _disposed.Task.IsCompleted;

        public void Dispose() => _disposed.TrySetResult();
    }
}
