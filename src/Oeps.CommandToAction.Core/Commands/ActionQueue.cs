using System.Diagnostics;

namespace Oeps.CommandToAction.Core.Commands;

public interface IActionKeyboard
{
    Task TypeTextAsync(string text, CancellationToken cancellationToken);
    Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken);
}

public interface IActionClock
{
    TimeSpan Elapsed { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed record ActionQueueState(int PendingCount, ActionJob? ActiveJob, string Status, Exception? Error = null);

/// <summary>One worker executes accepted jobs in receipt order. A failed partial job is never retried.</summary>
public sealed class ActionQueue : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<ActionJob> _jobs = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly IActionKeyboard _keyboard;
    private readonly IActionClock _clock;
    private readonly Task _worker;
    private CancellationTokenSource? _activeCancellation;
    private TaskCompletionSource? _activeFinished;
    private ActionJob? _activeJob;
    private TimeSpan? _lastFinished;
    private string _status = "Ready";
    private Exception? _error;
    private bool _cancelling;
    private int _cancellationRequests;
    private bool _disposed;

    public ActionQueue(IActionKeyboard keyboard, IActionClock? clock = null)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _clock = clock ?? new SystemActionClock();
        _worker = Task.Run(RunAsync);
    }

    public event Action<ActionQueueState>? StateChanged;
    public int PendingCount { get { lock (_gate) return _jobs.Count; } }
    public ActionQueueState State { get { lock (_gate) return Snapshot(); } }

    public bool Enqueue(ActionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.Timings.Validate();
        lock (_gate)
        {
            if (_disposed || _cancelling) return false;
            _jobs.Enqueue(job);
            if (_activeJob is null) _status = "Queued";
            _error = null;
            _available.Release();
        }
        Notify();
        return true;
    }

    /// <summary>Stops the current job and discards waiting jobs. Accepts fresh jobs after completion.</summary>
    public async Task CancelAsync()
    {
        Task? active;
        lock (_gate)
        {
            if (_disposed) return;
            _cancellationRequests++;
            _cancelling = true;
            _jobs.Clear();
            _activeCancellation?.Cancel();
            active = _activeFinished?.Task;
        }
        if (active is not null) await active.ConfigureAwait(false);
        lock (_gate)
        {
            _cancellationRequests--;
            _cancelling = _cancellationRequests > 0;
            _status = "Stopped";
            _error = null;
        }
        Notify();
    }

    private async Task RunAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try { await _available.WaitAsync(_shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            ActionJob job;
            CancellationTokenSource cancellation;
            TaskCompletionSource finished;
            lock (_gate)
            {
                if (_disposed || _cancelling || !_jobs.TryDequeue(out job!)) continue;
                _activeJob = job;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                _activeCancellation = cancellation;
                finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _activeFinished = finished;
                _status = "Running";
                _error = null;
            }
            Notify();

            string outcome;
            Exception? error = null;
            try
            {
                if (_lastFinished is { } previousFinish)
                {
                    var remaining = TimeSpan.FromMilliseconds(job.Timings.BetweenActionMs) - (_clock.Elapsed - previousFinish);
                    if (remaining > TimeSpan.Zero)
                        await _clock.DelayAsync(remaining, cancellation.Token).ConfigureAwait(false);
                }
                foreach (var step in job.Steps)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    switch (step)
                    {
                        case TextStep text:
                            await _keyboard.TypeTextAsync(text.Text, cancellation.Token).ConfigureAwait(false);
                            break;
                        case KeyStep key:
                            await _keyboard.PressKeyAsync(key.Key, cancellation.Token).ConfigureAwait(false);
                            break;
                        case DelayStep delay when delay.Milliseconds > 0:
                            await _clock.DelayAsync(TimeSpan.FromMilliseconds(delay.Milliseconds), cancellation.Token).ConfigureAwait(false);
                            break;
                    }
                }
                outcome = "Completed";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { outcome = "Stopped"; }
            catch (Exception exception) { outcome = "Failed"; error = exception; }
            finally { _lastFinished = _clock.Elapsed; }

            lock (_gate)
            {
                _activeJob = null;
                _activeCancellation = null;
                _activeFinished = null;
                _status = outcome;
                _error = error;
                finished.TrySetResult();
            }
            cancellation.Dispose();
            Notify();
        }
    }

    private ActionQueueState Snapshot() => new(_jobs.Count, _activeJob, _status, _error);

    private void Notify()
    {
        var handlers = StateChanged;
        if (handlers is null) return;
        var state = State;
        foreach (Action<ActionQueueState> handler in handlers.GetInvocationList())
        {
            // Display callbacks must not break the keyboard worker or scanner receive loop.
            try { handler(state); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _jobs.Clear();
            _shutdown.Cancel();
            _activeCancellation?.Cancel();
        }
        await _worker.ConfigureAwait(false);
        _shutdown.Dispose();
        _available.Dispose();
    }

    private sealed class SystemActionClock : IActionClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        public TimeSpan Elapsed => _stopwatch.Elapsed;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
    }
}
