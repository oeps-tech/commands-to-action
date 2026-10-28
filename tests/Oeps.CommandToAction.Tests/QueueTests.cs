using System.Collections.Concurrent;
using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.Tests;

public static class QueueTests
{
    [Test]
    public static async Task JobsStayFifoWithTheirOriginalCommandAndMinimumGap()
    {
        var clock = new FakeClock();
        var firstStarted = Signal();
        var releaseFirst = Signal();
        var allDone = Signal();
        var keyboard = new RecordingKeyboard(clock)
        {
            OnText = async (text, token) =>
            {
                if (text == "first")
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(token);
                    clock.Advance(100);
                }
            }
        };
        await using var queue = new ActionQueue(keyboard, clock);
        int completed = 0;
        queue.StateChanged += state =>
        {
            if (state.Status == "Completed" && Interlocked.Increment(ref completed) == 3) allDone.TrySetResult();
        };
        Assert.True(queue.Enqueue(Job(ActionCommand.Direct, "first")));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(queue.Enqueue(Job(ActionCommand.AddRight, "second")));
        Assert.True(queue.Enqueue(Job(ActionCommand.AddEnterAndUp, "third")));
        Assert.Equal(2, queue.PendingCount);
        releaseFirst.TrySetResult();
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("text:first|text:second|key:Right|text:third|key:Enter|key:Up", string.Join("|", keyboard.Calls.Select(call => call.Text)));
        Assert.Equal("0,350,350,600,600,600", string.Join(",", keyboard.Calls.Select(call => call.AtMilliseconds)));
        Assert.Equal(0, queue.PendingCount);
    }

    [Test]
    public static async Task CancellationDropsCurrentRemainderAndPendingJobsAndAllowsReconnect()
    {
        var clock = new FakeClock();
        var started = Signal();
        var completed = Signal();
        var keyboard = new RecordingKeyboard(clock)
        {
            OnText = async (text, token) =>
            {
                if (text == "in-progress")
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            }
        };
        await using var queue = new ActionQueue(keyboard, clock);
        queue.StateChanged += state => { if (state.Status == "Completed") completed.TrySetResult(); };
        queue.Enqueue(Job(ActionCommand.AddEnterAndUp, "in-progress"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        queue.Enqueue(Job(ActionCommand.Direct, "discard-me"));
        await queue.CancelAsync();
        Assert.Equal(0, queue.PendingCount);
        Assert.True(queue.State.ActiveJob is null);
        Assert.True(queue.Enqueue(Job(ActionCommand.Direct, "fresh-after-reconnect")));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("text:in-progress|text:fresh-after-reconnect", string.Join("|", keyboard.Calls.Select(call => call.Text)));
    }

    [Test]
    public static async Task CancellationRejectsNewJobsUntilActiveKeyboardCallHasActuallyStopped()
    {
        var clock = new FakeClock();
        var started = Signal();
        var cancelled = Signal();
        var releaseCleanup = Signal();
        var keyboard = new RecordingKeyboard(clock)
        {
            OnText = async (_, token) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    await releaseCleanup.Task;
                    throw;
                }
            }
        };
        await using var queue = new ActionQueue(keyboard, clock);
        queue.Enqueue(Job(ActionCommand.AddRight, "active"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopOne = queue.CancelAsync();
        var stopTwo = queue.CancelAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(stopOne.IsCompleted);
        Assert.False(stopTwo.IsCompleted);
        Assert.False(queue.Enqueue(Job(ActionCommand.Direct, "arrived-during-disconnect")));
        releaseCleanup.TrySetResult();
        await Task.WhenAll(stopOne, stopTwo).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(queue.State.ActiveJob is null);
        Assert.Equal("text:active", string.Join("|", keyboard.Calls.Select(call => call.Text)));
    }

    [Test]
    public static async Task PartialFailureIsReportedOnceWithoutRetryAndNextJobRuns()
    {
        var clock = new FakeClock();
        var completed = Signal();
        var keyboard = new RecordingKeyboard(clock)
        {
            OnText = (text, _) => text == "failing" ? Task.FromException(new IOException("injection rejected")) : Task.CompletedTask
        };
        await using var queue = new ActionQueue(keyboard, clock);
        var failures = new ConcurrentQueue<Exception>();
        queue.StateChanged += state =>
        {
            if (state.Error is { } error) failures.Enqueue(error);
            if (state.Status == "Completed") completed.TrySetResult();
        };
        queue.Enqueue(Job(ActionCommand.AddRight, "failing"));
        queue.Enqueue(Job(ActionCommand.Direct, "next"));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(failures.Any(error => error.Message == "injection rejected"));
        Assert.Equal("text:failing|text:next", string.Join("|", keyboard.Calls.Select(call => call.Text)));
    }

    [Test]
    public static async Task DisplayCallbackFailureCannotStopWorker()
    {
        var clock = new FakeClock();
        var completed = Signal();
        var keyboard = new RecordingKeyboard(clock);
        await using var queue = new ActionQueue(keyboard, clock);
        queue.StateChanged += _ => throw new Exception("display callback failed");
        queue.StateChanged += state => { if (state.Status == "Completed") completed.TrySetResult(); };
        Assert.True(queue.Enqueue(Job(ActionCommand.Direct, "still-runs")));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("text:still-runs", keyboard.Calls.Single().Text);
    }

    [Test]
    public static async Task NoExtraWaitIfPreviousCompletionWasAlreadyFarEnoughInThePast()
    {
        var clock = new FakeClock();
        var firstDone = Signal();
        var secondDone = Signal();
        var keyboard = new RecordingKeyboard(clock);
        await using var queue = new ActionQueue(keyboard, clock);
        int count = 0;
        queue.StateChanged += state =>
        {
            if (state.Status == "Completed")
            {
                if (Interlocked.Increment(ref count) == 1) firstDone.TrySetResult();
                else secondDone.TrySetResult();
            }
        };
        queue.Enqueue(Job(ActionCommand.Direct, "first"));
        await firstDone.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(1000);
        queue.Enqueue(Job(ActionCommand.Direct, "second"));
        await secondDone.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("0,1000", string.Join(",", keyboard.Calls.Select(call => call.AtMilliseconds)));
        Assert.Equal(0, clock.Delays.Count);
    }

    private static ActionJob Job(ActionCommand command, string raw)
    {
        Assert.True(ActionPlanner.TryCreate(command, raw, new(), out var job));
        return job!;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeClock : IActionClock
    {
        private long _ticks;
        public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));
        public ConcurrentQueue<TimeSpan> Delays { get; } = new();
        public void Advance(int milliseconds) => Interlocked.Add(ref _ticks, TimeSpan.FromMilliseconds(milliseconds).Ticks);
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Enqueue(delay);
            Interlocked.Add(ref _ticks, delay.Ticks);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingKeyboard(FakeClock clock) : IActionKeyboard
    {
        public ConcurrentQueue<(string Text, long AtMilliseconds)> Calls { get; } = new();
        public Func<string, CancellationToken, Task>? OnText { get; init; }
        public async Task TypeTextAsync(string text, CancellationToken cancellationToken)
        {
            Calls.Enqueue(("text:" + text, (long)clock.Elapsed.TotalMilliseconds));
            if (OnText is { } handler) await handler(text, cancellationToken);
        }
        public Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue(("key:" + key, (long)clock.Elapsed.TotalMilliseconds));
            return Task.CompletedTask;
        }
    }
}
