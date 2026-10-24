namespace SmartPlagiarism.Engine.Ocr;

/// <summary>
/// Runs calls one at a time on an engine that is not thread-safe and cannot be
/// interrupted - the in-process Tesseract engine - and gives up on a call that
/// runs past its time limit.
///
/// A native call cannot be stopped, so giving up on one means leaving it running:
/// its engine is retired, kept by that call alone and disposed once the call
/// finally returns, and the next call gets a fresh engine. Other documents carry
/// on instead of queueing behind one stuck picture.
///
/// Only one engine is retired at a time. Should another call overrun while one
/// still is, that call keeps its engine and the next caller waits for it, so a
/// run of hostile pictures cannot pile up engines and the memory each one holds.
/// </summary>
internal sealed class TimeLimitedEngine<TEngine> : IDisposable
    where TEngine : class, IDisposable
{
    private readonly Func<TEngine> _createEngine;
    private readonly TimeSpan _timeLimit;

    // Never disposed: a call left running past its time limit releases it when it
    // finally returns, which may be after this object is disposed.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private TEngine? _engine;
    private Task _retired = Task.CompletedTask;
    private bool _disposed;

    /// <param name="engine">The engine to start with.</param>
    /// <param name="createEngine">Makes the replacement for a retired engine.</param>
    /// <param name="timeLimit">How long one call may run.</param>
    public TimeLimitedEngine(TEngine engine, Func<TEngine> createEngine, TimeSpan timeLimit)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(createEngine);

        _engine = engine;
        _createEngine = createEngine;
        _timeLimit = timeLimit;
    }

    /// <exception cref="TimeoutException">The call ran past the time limit and was given up on.</exception>
    public async Task<T> RunAsync<T>(Func<TEngine, T> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _gate.WaitAsync(cancellationToken);

        TEngine engine;
        Task<T> call;

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            engine = _engine ??= _createEngine();

            // A thread of its own, since the native call blocks it for as long as it runs.
            call = Task.Factory.StartNew(
                () => work(engine),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        catch
        {
            _gate.Release();
            throw;
        }

        var callKeepsGate = false;

        try
        {
            return await call.WaitAsync(_timeLimit, cancellationToken);
        }
        catch (TimeoutException) when (_retired.IsCompleted)
        {
            _engine = null;
            _retired = AfterwardsAsync(call, engine.Dispose);
            throw;
        }
        catch (Exception) when (!call.IsCompleted)
        {
            // Timed out while another engine is still retired, or cancelled because
            // the server is stopping: the call keeps the engine, and the gate with it.
            callKeepsGate = true;
            _ = AfterwardsAsync(call, () => _gate.Release());
            throw;
        }
        finally
        {
            if (!callKeepsGate)
            {
                _gate.Release();
            }
        }
    }

    public void Dispose()
    {
        // An engine still busy with a call cannot be freed underneath it without
        // crashing the process; the server is stopping, and its exit frees it.
        if (!_gate.Wait(TimeSpan.Zero))
        {
            return;
        }

        try
        {
            _disposed = true;
            _engine?.Dispose();
            _engine = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs <paramref name="then"/> once a call that was given up on returns, however it ends.</summary>
    private static async Task AfterwardsAsync(Task call, Action then)
    {
        // Its outcome no longer matters: the caller has already been told it timed out.
        await call.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        then();
    }
}
