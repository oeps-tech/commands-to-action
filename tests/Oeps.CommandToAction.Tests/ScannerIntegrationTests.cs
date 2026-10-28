using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Scanning;
using Scanner.Client;

namespace Oeps.CommandToAction.Tests;

public static class ScannerIntegrationTests
{
    [Test]
    public static async Task BundledSdkRoutesCommandsAndRepeatedRawLabelsInWireOrder()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        const string escaped = "café \"\\\t\r\n😀 +^%{}";
        string Command(string value) => JsonSerializer.Serialize(new { label_version = "C", command = value });
        string Raw(string value) => JsonSerializer.Serialize(new { label_version = "X", raw = value });
        var messages = new[]
        {
            Command("Direct"), Raw("first"), Raw("first"), Raw(escaped),
            Command("Add Right"), Raw("last"),
            "{\"label_version\":1,\"oeps_pn\":\"OEPS123456\",\"lot\":\"1234-ABC-A1B2\",\"quantity\":\"1\"}",
            Command("Disable"), Raw("ignored"), Command("Direct"), Raw("tail")
        };

        var keyboard = new RecordingKeyboard();
        await using var queue = new ActionQueue(keyboard);
        var dispatcher = new LabelDispatcher(queue)
        {
            Timings = new ActionTimings { InitMs = 0, SnMs = 0, TabMs = 0, CharMs = 0, BetweenActionMs = 0 }
        };
        var commands = new ConcurrentQueue<ActionCommand>();
        dispatcher.CommandChanged += commands.Enqueue;
        var results = new ConcurrentQueue<DispatchResult>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.StateChanged += state =>
        {
            if (state.PendingCount == 0 && state.ActiveJob is null && keyboard.Events.Count == 6) drained.TrySetResult();
        };

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeAsync(listener, messages, token);
        await using var scanner = new ScannerConnection();
        var count = 0;
        scanner.JsonReceived += json =>
        {
            results.Enqueue(dispatcher.HandleJson(json));
            if (Interlocked.Increment(ref count) == messages.Length) received.TrySetResult();
        };
        try
        {
            await scanner.ConnectAsync(null, port, thisComputer: true, cancellationToken: token);
            await received.Task.WaitAsync(token);
            Assert.True(scanner.Connected);
            Assert.Equal(ActionCommand.Direct, dispatcher.SelectedCommand);
            // Hold the first job until every command arrives to test receipt-time snapshots.
            keyboard.Release.TrySetResult();
            await drained.Task.WaitAsync(token);
            Assert.Equal("Direct|AddRight|Disable|Direct", string.Join("|", commands));
            Assert.Equal("CommandSelected|Queued|Queued|Queued|CommandSelected|Queued|Ignored|CommandSelected|Ignored|CommandSelected|Queued",
                string.Join("|", results));
            Assert.Equal("T:first|T:first|T:" + escaped + "|T:last|K:Right|T:tail", string.Join("|", keyboard.Events));
            await scanner.CloseAsync().WaitAsync(token);
            Assert.False(scanner.Connected);
            await server.WaitAsync(token);
        }
        finally
        {
            timeout.Cancel();
            keyboard.Release.TrySetResult();
            listener.Stop();
            try { await server; } catch (OperationCanceledException) { } catch (SocketException) { }
        }
    }

    // A minimal real WebSocket endpoint avoids HttpListener URL reservations/admin rights.
    private static async Task ServeAsync(TcpListener listener, IReadOnlyList<string> messages, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var stream = client.GetStream();
        var header = new List<byte>();
        var next = new byte[1];
        while (header.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(next, token) == 0) throw new IOException("SDK closed before its WebSocket handshake.");
            header.Add(next[0]);
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        Assert.Equal("GET /labels HTTP/1.1", lines[0]);
        var keyLine = lines.Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase));
        var key = keyLine[(keyLine.IndexOf(':') + 1)..].Trim();
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n"), token);
        using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
        foreach (var message in messages)
            await socket.SendAsync(Encoding.UTF8.GetBytes(message).AsMemory(), WebSocketMessageType.Text, endOfMessage: true, token);
        var buffer = new byte[256];
        try
        {
            var response = await socket.ReceiveAsync(buffer.AsMemory(), token);
            Assert.Equal(WebSocketMessageType.Close, response.MessageType);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closed", token);
        }
        catch (WebSocketException exception) when (exception.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // SDK CloseAsync may cancel its receive loop and close the transport directly.
            // Either shutdown path must terminate this server-side receive operation.
        }
    }

    private sealed class RecordingKeyboard : IActionKeyboard
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task TypeTextAsync(string text, CancellationToken cancellationToken)
        {
            await Release.Task.WaitAsync(cancellationToken);
            Events.Enqueue("T:" + text);
        }
        public Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Enqueue("K:" + key);
            return Task.CompletedTask;
        }
    }
}
