using System.Collections.Concurrent;
using System.Text.Json;
using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Scanning;

namespace Oeps.CommandToAction.Tests;

public static class DispatcherTests
{
    [Test]
    public static async Task DisabledStartupIgnoresRawButStillAcceptsKnownCommandLabels()
    {
        var keyboard = new Recorder();
        await using var queue = new ActionQueue(keyboard);
        var dispatcher = new LabelDispatcher(queue);
        ActionCommand? selected = null;
        dispatcher.CommandChanged += command => selected = command;
        Assert.Equal(ActionCommand.Disable, dispatcher.SelectedCommand);
        Assert.Equal(DispatchResult.Ignored, dispatcher.HandleJson(Raw("OEPS-ABCD")));
        Assert.Equal(DispatchResult.CommandSelected, dispatcher.HandleJson(Command("Odoo IN")));
        Assert.Equal(ActionCommand.OdooIn, dispatcher.SelectedCommand);
        Assert.Equal<ActionCommand?>(ActionCommand.OdooIn, selected);
        Assert.Equal(DispatchResult.Ignored, dispatcher.HandleJson(Command("Odoo in")));
        Assert.Equal(ActionCommand.OdooIn, dispatcher.SelectedCommand);
        Assert.Equal(DispatchResult.CommandSelected, dispatcher.HandleJson(Command("Disable")));
        Assert.Equal(DispatchResult.Ignored, dispatcher.HandleJson(Raw("anything")));
        Assert.Equal(0, keyboard.Calls.Count);
    }

    [Test]
    public static async Task StructuredVersionsAndMalformedMessagesDoNotTypeOrChangeTheSelection()
    {
        var keyboard = new Recorder();
        await using var queue = new ActionQueue(keyboard);
        var dispatcher = new LabelDispatcher(queue);
        dispatcher.SelectCommand(ActionCommand.Direct);
        foreach (var json in new[]
        {
            "{\"label_version\":1,\"raw\":\"not an X label\"}",
            "{\"label_version\":2,\"command\":\"Disable\"}",
            "{\"label_version\":3,\"raw\":\"not an X label\"}",
            "{\"label_version\":\"future\",\"raw\":\"not an X label\"}"
        }) Assert.Equal(DispatchResult.Ignored, dispatcher.HandleJson(json));
        foreach (var json in new[]
        {
            "broken JSON", "[]", "null", "{}", "{\"label_version\":\"C\"}",
            "{\"label_version\":\"C\",\"command\":null}",
            "{\"label_version\":\"X\",\"raw\":123}", "{\"label_version\":\"X\"}"
        }) Assert.Equal(DispatchResult.Invalid, dispatcher.HandleJson(json));
        Assert.Equal(ActionCommand.Direct, dispatcher.SelectedCommand);
        Assert.Equal(0, keyboard.Calls.Count);
    }

    [Test]
    public static async Task RawTextDecodesJsonControlsAndNeverDispatchesEmbeddedCommandSyntax()
    {
        var keyboard = new Recorder();
        await using var queue = new ActionQueue(keyboard);
        var dispatcher = new LabelDispatcher(queue);
        dispatcher.SelectCommand(ActionCommand.Direct);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.StateChanged += state => { if (state.Status == "Completed") completed.TrySetResult(); };
        const string content = "C*Disable*bad-crc\t\r\n+^{Tab} é漢🙂";
        Assert.Equal(DispatchResult.Queued, dispatcher.HandleJson(Raw(content)));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ActionCommand.Direct, dispatcher.SelectedCommand);
        Assert.Equal(content, keyboard.Calls.Single());
    }

    [Test]
    public static async Task ScannedAndLocalCommandChangesOnlyAffectSubsequentRawLabels()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var keyboard = new Recorder
        {
            BeforeText = async (text, token) =>
            {
                if (text == "first")
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task.WaitAsync(token);
                }
            }
        };
        await using var queue = new ActionQueue(keyboard);
        var dispatcher = new LabelDispatcher(queue) { Timings = new() { BetweenActionMs = 0 } };
        int count = 0;
        queue.StateChanged += state =>
        {
            if (state.Status == "Completed" && Interlocked.Increment(ref count) == 2) completed.TrySetResult();
        };
        dispatcher.SelectCommand(ActionCommand.Direct);
        Assert.Equal(DispatchResult.Queued, dispatcher.HandleJson(Raw("first")));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DispatchResult.CommandSelected, dispatcher.HandleJson(Command("Add Right")));
        Assert.Equal(DispatchResult.Queued, dispatcher.HandleJson(Raw("second")));
        dispatcher.SelectCommand(ActionCommand.Disable);
        Assert.Equal(DispatchResult.Ignored, dispatcher.HandleJson(Raw("ignored-third")));
        releaseFirst.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("first|second|[Right]", string.Join("|", keyboard.Calls));
    }

    private static string Raw(string raw) => JsonSerializer.Serialize(new { label_version = "X", raw });
    private static string Command(string command) => JsonSerializer.Serialize(new { label_version = "C", command });

    private sealed class Recorder : IActionKeyboard
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Func<string, CancellationToken, Task>? BeforeText { get; init; }
        public async Task TypeTextAsync(string text, CancellationToken cancellationToken)
        {
            if (BeforeText is { } handler) await handler(text, cancellationToken);
            Calls.Enqueue(text);
        }
        public Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken)
        {
            Calls.Enqueue($"[{key}]");
            return Task.CompletedTask;
        }
    }
}
