using System.Collections.Concurrent;
using System.Text.Json;
using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Scanning;

namespace Oeps.CommandToAction.App;

internal sealed partial class MainForm
{
    private SmokeKeyboard? _smokeKeyboard;
    private int _smokeBarcodeNotifications;

    private void RecordSmokeBarcode(Bitmap image) => image.Save(
        Path.Combine(_paths.UserDataRoot, "command-" + _dispatcher.SelectedCommand + ".png"), System.Drawing.Imaging.ImageFormat.Png);

    private async Task RunSmokeAsync()
    {
        Directory.CreateDirectory(_paths.UserDataRoot);
        var report = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.Add("PASS " + message);
        }
        try
        {
            Check(!_camera.Checked && !_camera.Enabled, "Camera stays unchecked and disabled.");
            Check(_command.Items.Cast<string>().SequenceEqual(new[] { "Odoo IN", "Odoo IN Moves", "Odoo OUT Moves", "Add Enter and Up", "Add Right", "Direct", "Disable" }), "Command dropdown order matches specification.");
            Check(_command.Text == "Disable", "Startup command is Disable.");
            foreach (var descriptor in CommandCatalog.All)
            {
                _command.SelectedIndex = _command.Items.IndexOf(descriptor.Name);
                _barcode.PerformClick();
                using var saved = new Bitmap(Path.Combine(_paths.UserDataRoot, "command-" + descriptor.Command + ".png"));
                Check(saved.Width == 1024 && saved.Height == 1024 && Math.Abs(saved.HorizontalResolution - 300) < 1,
                    descriptor.Name + " copies a 1024px, 300 DPI image.");
                // Decode the actual saved image at a paste/print-sized resolution.
                var pixels = new byte[256 * 256];
                for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++) pixels[y * 256 + x] = saved.GetPixel(x * 4, y * 4).R;
                var decoded = new ZXing.Datamatrix.DataMatrixReader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(
                    new ZXing.PlanarYUVLuminanceSource(pixels, 256, 256, 0, 0, 256, 256, false))));
                Check(decoded is not null && Scanner.Contracts.PayloadValidator.Validate(decoded.Text).Data?.Command == descriptor.Name,
                    descriptor.Name + " copied image decodes to its validated command.");
            }
            Check(_smokeBarcodeNotifications == 7, "Every successful barcode copy shows a confirmation.");
            _reader.Checked = false;
            Check(!_connect.Enabled, "Connect requires reader selection.");
            _reader.Checked = true;
            Check(_connect.Enabled, "Reader-only connection is available.");
            _server.SelectedIndex = 1;
            Check(_host.Enabled, "External server address is editable.");
            _server.SelectedIndex = 0;
            Check(!_host.Enabled, "Local computer needs no server address.");
            Check(_dispatcher.HandleJson("{\"label_version\":\"C\",\"command\":\"Direct\"}") == DispatchResult.CommandSelected, "C label selects a command.");
            await Task.Delay(75);
            Check(_command.Text == "Direct", "Scanner command updates dropdown.");
            _dispatcher.Timings = new ActionTimings { BetweenActionMs = 0 };
            _dispatcher.HandleJson(JsonSerializer.Serialize(new { label_version = "X", raw = "a+{}%é" }));
            _dispatcher.HandleJson(JsonSerializer.Serialize(new { label_version = "X", raw = "a+{}%é" }));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_smokeKeyboard!.Text.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
            Check(_smokeKeyboard.Text.SequenceEqual(new[] { "a+{}%é", "a+{}%é" }), "Repeated X labels reach keyboard abstraction as literal text in order.");
            _command.SelectedIndex = 0;
            Check(_dispatcher.SelectedCommand == ActionCommand.OdooIn, "Local selection updates active command.");
            Check(_dispatcher.HandleJson("{\"label_version\":\"X\",\"raw\":\"invalid\"}") == DispatchResult.Invalid, "Odoo invalid format is ignored.");
            await _queue.CancelAsync();
            _connected = true;
            _accepting = true;
            SetConnectionControls();
            _command.SelectedIndex = 4;
            _dispatcher.HandleJson(JsonSerializer.Serialize(new { label_version = "X", raw = "hold-for-discard" }));
            await _smokeKeyboard.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _dispatcher.HandleJson(JsonSerializer.Serialize(new { label_version = "X", raw = "pending-discard" }));
            await Task.Delay(50);
            Check(_discard.Enabled && _queue.PendingCount == 1, "Discard Queue is available for active and pending jobs.");
            _discard.PerformClick();
            await _discardOperation;
            Check(_queue.State.ActiveJob is null && _queue.PendingCount == 0, "Discard Queue cancels the active action and clears pending labels.");
            Check(_connected && _accepting && _connect.Text == "Disconnect", "Discard Queue keeps the reader connection active.");
            Check(!_smokeKeyboard.Text.Contains("pending-discard") && _smokeKeyboard.Keys.IsEmpty, "Discarded jobs and the active action's remaining keys are not sent.");
            _command.SelectedIndex = 5;
            _dispatcher.HandleJson(JsonSerializer.Serialize(new { label_version = "X", raw = "fresh-after-discard" }));
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_smokeKeyboard.Text.Contains("fresh-after-discard") && DateTime.UtcNow < deadline) await Task.Delay(20);
            Check(_smokeKeyboard.Text.Contains("fresh-after-discard"), "New labels execute after discarding without reconnecting.");
            _connected = _accepting = false;
            SetConnectionControls();
            _command.SelectedIndex = 6;
            _dispatcher.Timings = _settings.Timings;
            await _queue.CancelAsync();
            _update.Text = "Update available";
            await Task.Delay(100);
            _actionStatus.Text = "Select a command, connect, then focus the destination field.";
            foreach (var control in new Control[] { _server, _reader, _command, _info, _gear, _barcode, _connect, _discard, _update, _versionStatus })
                Check(RectangleToScreen(ClientRectangle).Contains(control.RectangleToScreen(control.ClientRectangle)), control.AccessibleName ?? control.Text + " fits the window.");
            Check(_info.PointToScreen(Point.Empty).X > _command.PointToScreen(Point.Empty).X + _command.Width
                && _gear.PointToScreen(Point.Empty).X > _info.PointToScreen(Point.Empty).X, "Info and gear appear to the right of the command.");
            Check(_barcode.PointToScreen(Point.Empty).X > _gear.PointToScreen(Point.Empty).X + _gear.Width
                && _barcode.Height == _command.Height && _barcode.Top == _command.Top, "Barcode is the third button and matches the dropdown height and alignment.");
            CaptureWindow(this, Path.Combine(_paths.UserDataRoot, "main-window.png"));
            Check(_info.Height == _command.Height && _gear.Height == _command.Height
                && _info.Top == _command.Top && _gear.Top == _command.Top,
                $"Info and gear match dropdown height/alignment (dropdown {_command.Height}/{_command.Top}, info {_info.Height}/{_info.Top}, gear {_gear.Height}/{_gear.Top}).");
            Check(_discard.PointToScreen(Point.Empty).Y >= _connect.PointToScreen(Point.Empty).Y + _connect.Height
                && _discard.BackColor == Color.White, "Discard Queue sits below Connect with a white background.");
            Check(_versionStatus.PointToScreen(Point.Empty).X > _update.PointToScreen(Point.Empty).X + _update.Width,
                "Version is at the bottom right, after the update indicator.");
            CaptureWindow(this, Path.Combine(_paths.UserDataRoot, "main-window.png"));
            using (var dialog = new CommandInfoDialog(ActionCommand.OdooInMoves))
            {
                dialog.Show(this);
                await Task.Delay(100);
                CaptureWindow(dialog, Path.Combine(_paths.UserDataRoot, "command-info.png"));
                dialog.Close();
            }
            using (var dialog = new TimingDialog(_settings.Timings))
            {
                dialog.Show(this);
                await Task.Delay(100);
                Check(dialog.Timings == _settings.Timings, "Timing dialog preserves all five intervals.");
                Check(new ActionTimings().TabMs == 200, "TAB default is 200 ms.");
                CaptureWindow(dialog, Path.Combine(_paths.UserDataRoot, "timing-dialog.png"));
                dialog.Close();
            }
            report.Add("No real scanner connection, native keyboard injection or user clipboard changes were used.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); Environment.ExitCode = 1; }
        finally
        {
            File.WriteAllLines(Path.Combine(_paths.UserDataRoot, "ui-smoke.txt"), report);
            Close();
        }
    }

    private static void CaptureWindow(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private sealed class SmokeKeyboard : IActionKeyboard
    {
        internal ConcurrentQueue<string> Text { get; } = new();
        internal ConcurrentQueue<ActionKey> Keys { get; } = new();
        internal TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task TypeTextAsync(string text, CancellationToken cancellationToken)
        {
            Text.Enqueue(text);
            if (text == "hold-for-discard")
            {
                Held.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
        public Task PressKeyAsync(ActionKey key, CancellationToken cancellationToken) { Keys.Enqueue(key); return Task.CompletedTask; }
    }
}
