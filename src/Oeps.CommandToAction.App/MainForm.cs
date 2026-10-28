using System.Reflection;
using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Configuration;
using Oeps.CommandToAction.Core.Scanning;
using Oeps.CommandToAction.Core.Updates;
using Scanner.Client;

namespace Oeps.CommandToAction.App;

internal sealed partial class MainForm : Form
{
    private readonly AppConfiguration _config;
    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private readonly string[] _args;
    private readonly UserSettings _settings;
    private readonly ActionQueue _queue;
    private readonly LabelDispatcher _dispatcher;
    private readonly object _sessionGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly ToolTip _tip = new();
    private readonly ComboBox _server = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Camera server" };
    private readonly TextBox _host = new() { Dock = DockStyle.Fill, PlaceholderText = "Server IP or host name", AccessibleName = "Server address" };
    private readonly CheckBox _camera = new() { Text = "Use Camera:", Enabled = false, Checked = false, AutoSize = true, CheckAlign = ContentAlignment.MiddleRight };
    private readonly CheckBox _reader = new() { Text = "Use Reader:", AutoSize = true, CheckAlign = ContentAlignment.MiddleRight };
    private readonly ComboBox _command = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "Command" };
    private readonly Button _info = SmallButton("i", "Explain selected command");
    private readonly Button _gear = SmallButton("⚙", "Configure action timing");
    private readonly Button _barcode = SmallButton("", "Copy command Data Matrix");
    private readonly Action<Bitmap> _copyBarcodeImage;
    private readonly Action _showBarcodeCopied;
    private readonly Button _connect = new() { Text = "Connect", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 103, 192), ForeColor = Color.White };
    private readonly Button _discard = new() { Text = "Discard Queue", AccessibleName = "Discard Queue", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(0, 93, 174) };
    private readonly Label _versionStatus = new() { AutoSize = true, Anchor = AnchorStyles.Right, TextAlign = ContentAlignment.MiddleRight, Margin = new Padding(12, 0, 0, 0), ForeColor = Color.FromArgb(80, 92, 106) };
    private readonly Label _connectionStatus = StatusLabel("Disconnected");
    private readonly Label _actionStatus = StatusLabel("Select a command, connect, then focus the destination field.");
    private readonly Label _queueStatus = StatusLabel("Queue: 0");
    private readonly LinkLabel _update = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Anchor = AnchorStyles.Right, LinkColor = Color.FromArgb(0, 103, 192) };
    private readonly string _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    private ScannerConnection? _scanner;
    private CancellationTokenSource? _connectionCancellation;
    private Task _connectionOperation = Task.CompletedTask;
    private Task _discardOperation = Task.CompletedTask;
    private bool _accepting, _connecting, _disconnecting, _discarding, _connected, _changingCommand, _closing, _canClose, _checkingRelease, _waitingForTarget;
    private DateTimeOffset _nextReleaseCheck;

    public MainForm(AppConfiguration config, AppPaths paths, HttpClient http, string[] args)
    {
        (_config, _paths, _http, _args) = (config, paths, http, args);
        _settings = UserSettings.Load(paths.SettingsFile);
        IActionKeyboard keyboard;
        if (args.Contains("--ui-smoke")) keyboard = _smokeKeyboard = new SmokeKeyboard();
        else
        {
            var native = new WindowsActionKeyboard();
            native.WaitingForTargetChanged += waiting => Ui(() => { _waitingForTarget = waiting; ShowQueueState(); });
            keyboard = native;
        }
        _queue = new ActionQueue(keyboard);
        _dispatcher = new LabelDispatcher(_queue) { Timings = _settings.Timings };
        _copyBarcodeImage = args.Contains("--ui-smoke") ? RecordSmokeBarcode : CommandBarcodeImage.CopyToClipboard;
        _showBarcodeCopied = args.Contains("--ui-smoke") ? () => _smokeBarcodeNotifications++ :
            () => MessageBox.Show(this, "Data Matrix code copied to clipboard.", "OEPS Command to Action", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Text = "OEPS Command to Action";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 370);
        MinimumSize = new Size(560, 406);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 247, 250);
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Oeps.AppIcon"))
            if (stream is not null) Icon = new Icon(stream);
        _server.Items.AddRange(["Local computer", "External"]);
        _server.SelectedIndex = _settings.CameraExternal ? 1 : 0;
        _host.Text = _settings.CameraIp;
        _reader.Checked = _settings.UseReader;
        foreach (var item in CommandCatalog.All) _command.Items.Add(item.Name);
        _command.SelectedIndex = _command.Items.IndexOf("Disable");
        BuildLayout();
        _gear.Font = new Font("Segoe UI Symbol", 10);
        _connect.FlatAppearance.BorderSize = 0;
        _discard.FlatAppearance.BorderColor = _discard.ForeColor;
        _server.SelectedIndexChanged += (_, _) => SetConnectionControls();
        _reader.CheckedChanged += (_, _) => SetConnectionControls();
        _command.SelectedIndexChanged += (_, _) =>
        {
            if (!_changingCommand && CommandCatalog.TryParse(_command.Text, out var command)) _dispatcher.SelectCommand(command);
        };
        _dispatcher.CommandChanged += _ => Ui(() =>
        {
            // Read latest state so a delayed UI callback cannot overwrite a newer scanner command.
            _changingCommand = true;
            _command.SelectedIndex = CommandCatalog.All.ToList().FindIndex(c => c.Command == _dispatcher.SelectedCommand);
            _changingCommand = false;
            ShowQueueState();
        });
        _dispatcher.Message += message => Ui(() => _actionStatus.Text = message);
        _queue.StateChanged += state => Ui(() =>
        {
            ShowQueueState();
            if (state.Error is not null) _actionStatus.Text = "Action failed; not retried. " + state.Error.Message;
        });
        _connect.Click += async (_, _) => { _connectionOperation = ToggleConnectionAsync(); await _connectionOperation; };
        _discard.Click += async (_, _) => { _discardOperation = DiscardQueueAsync(); await _discardOperation; };
        _info.Click += (_, _) => ShowCommandInfo();
        _gear.Click += (_, _) => ConfigureTiming();
        _barcode.Click += (_, _) => CopyCommandBarcode();
        _barcode.Paint += (_, e) => PaintBarcodeIcon(e.Graphics);
        _update.LinkClicked += (_, _) => _actionStatus.Text = "Restart from the OEPS shortcut to install the update. Choose ‘Not now’ to skip.";
        _tip.SetToolTip(_camera, "Camera input is reserved for future use and is disabled.");
        _tip.SetToolTip(_reader, $"Barcode reader on port {config.ReaderPort}");
        _tip.SetToolTip(_info, "What does this command do?");
        _tip.SetToolTip(_gear, "Set action delays");
        _tip.SetToolTip(_barcode, "Copy a Data Matrix image that selects this command");
        _tip.SetToolTip(_discard, "Stop the current action and discard pending labels. Keep the reader connected.");
        _timer.Tick += async (_, _) =>
        {
            if (_closing) return;
            if (_connected && _scanner is { Connected: false })
            {
                _connectionOperation = DisconnectAsync();
                await _connectionOperation;
                _connectionStatus.Text = "Reader disconnected. Connect to retry.";
            }
            if (DateTimeOffset.UtcNow >= _nextReleaseCheck) await CheckReleaseAsync();
        };
        Shown += async (_, _) =>
        {
            AppInstanceCoordinator.SignalReadyFromArguments(args);
            if (args.Contains("--ui-smoke")) { await RunSmokeAsync(); return; }
            _timer.Start();
            if (_settings.LoadError is not null) _actionStatus.Text = _settings.LoadError;
            await CheckReleaseAsync();
        };
        FormClosing += OnClosing;
        SetConnectionControls();
    }

    private static Button SmallButton(string text, string name) => new()
    {
        Text = text, AccessibleName = name, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
        BackColor = Color.White, ForeColor = Color.FromArgb(0, 93, 174), Margin = new Padding(6, 0, 0, 0)
    };
    private static Label StatusLabel(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true, ForeColor = Color.FromArgb(80, 92, 106), Margin = Padding.Empty
    };

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 18, 20, 12), ColumnCount = 1, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 36, 38, 38, 32, 48 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var serverRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        serverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        serverRow.Controls.Add(StatusLabel("Camera Server"), 0, 0);
        serverRow.Controls.Add(_server, 1, 0); serverRow.Controls.Add(_host, 2, 0);
        layout.Controls.Add(serverRow, 0, 0);
        var sources = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(112, 1, 0, 0) };
        _camera.Margin = new Padding(3, 0, 24, 0); _reader.Margin = new Padding(0);
        sources.Controls.AddRange([_camera, _reader]); layout.Controls.Add(sources, 0, 1);
        var commands = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Margin = new Padding(0, 0, 0, 6) };
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        commands.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        _command.Dock = DockStyle.None;
        _command.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _command.Margin = new Padding(3, 3, 3, 0);
        foreach (var button in new[] { _info, _gear, _barcode })
        {
            button.Dock = DockStyle.None;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            button.Margin = new Padding(6, 3, 0, 0);
            button.Height = _command.Height;
        }
        _command.SizeChanged += (_, _) => _info.Height = _gear.Height = _barcode.Height = _command.Height;
        commands.Layout += (_, _) =>
        {
            foreach (var button in new[] { _info, _gear, _barcode })
                if (button.Height != _command.Height) button.Height = _command.Height;
        };
        commands.Controls.Add(StatusLabel("Command"), 0, 0); commands.Controls.Add(_command, 1, 0);
        commands.Controls.Add(_info, 2, 0); commands.Controls.Add(_gear, 3, 0);
        commands.Controls.Add(_barcode, 4, 0);
        layout.Controls.Add(commands, 0, 2);
        layout.Controls.Add(_connectionStatus, 0, 3);
        layout.Controls.Add(_actionStatus, 0, 4);
        layout.Controls.Add(_connect, 0, 6);
        layout.Controls.Add(_discard, 0, 7);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _versionStatus.Text = "v" + _version; _versionStatus.Font = new Font(Font.FontFamily, 8.5f);
        _queueStatus.Font = new Font(Font.FontFamily, 8.5f); _update.Font = new Font(Font.FontFamily, 8.5f);
        footer.Controls.Add(_queueStatus, 0, 0); footer.Controls.Add(_update, 1, 0); footer.Controls.Add(_versionStatus, 2, 0);
        layout.Controls.Add(footer, 0, 8); Controls.Add(layout);
    }

    private void ShowCommandInfo()
    {
        using var dialog = new CommandInfoDialog(_dispatcher.SelectedCommand) { Icon = Icon };
        dialog.ShowDialog(this);
    }

    private void CopyCommandBarcode()
    {
        var command = _dispatcher.SelectedCommand;
        try
        {
            using var image = CommandBarcodeImage.Create(command);
            _copyBarcodeImage(image);
            _showBarcodeCopied();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            MessageBox.Show(this, "Could not copy the Data Matrix image. Please try again.\n\n" + ex.Message,
                "Clipboard unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void PaintBarcodeIcon(Graphics graphics)
    {
        var scale = _barcode.DeviceDpi / 96f;
        var left = (_barcode.ClientSize.Width - 19 * scale) / 2;
        var top = (_barcode.ClientSize.Height - 13 * scale) / 2;
        using var brush = new SolidBrush(_barcode.ForeColor);
        foreach (var width in new[] { 1, 2, 1, 3, 1, 2, 2 })
        {
            graphics.FillRectangle(brush, (int)Math.Round(left), (int)Math.Round(top),
                Math.Max(1, (int)Math.Round(width * scale)), (int)Math.Round(13 * scale));
            left += (width + 1) * scale;
        }
    }

    private void ConfigureTiming()
    {
        using var dialog = new TimingDialog(_dispatcher.Timings) { Icon = Icon };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _settings.Timings = dialog.Timings;
        _dispatcher.Timings = dialog.Timings;
        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings.CameraExternal = _server.SelectedIndex == 1;
        _settings.CameraIp = _host.Text.Trim();
        _settings.UseReader = _reader.Checked;
        try { _settings.Save(_paths.SettingsFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _actionStatus.Text = "Settings could not be saved: " + ex.Message; }
    }

    private void SetConnectionControls()
    {
        var idle = !_connecting && !_disconnecting && !_connected && !_closing;
        _server.Enabled = _reader.Enabled = idle;
        _host.Enabled = idle && _server.SelectedIndex == 1;
        _camera.Enabled = _camera.Checked = false;
        _connect.Enabled = !_closing && !_connecting && !_disconnecting && !_discarding && (_connected || _reader.Checked);
        _connect.Text = _disconnecting ? "Disconnecting…" : _connecting ? "Connecting…" : _connected ? "Disconnect" : "Connect";
        SetDiscardControl();
    }

    private async Task ToggleConnectionAsync()
    {
        if (_connecting || _disconnecting || _discarding || _closing) return;
        if (_connected) { await DisconnectAsync(); return; }
        if (!_reader.Checked) return;
        var local = _server.SelectedIndex == 0;
        var host = _host.Text.Trim();
        if (!local && (host.Length == 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown))
        { _connectionStatus.Text = "Enter a valid server IP address or host name."; _host.Focus(); return; }
        SaveSettings();
        _connecting = true; SetConnectionControls();
        _connectionStatus.Text = $"Connecting to {(local ? "127.0.0.1" : host)}:{_config.ReaderPort}…";
        _connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var scanner = new ScannerConnection();
        scanner.JsonReceived += json =>
        {
            lock (_sessionGate)
                if (_accepting && ReferenceEquals(_scanner, scanner)) _dispatcher.HandleJson(json);
        };
        scanner.StateChanged += state => Ui(() =>
        {
            if (ReferenceEquals(_scanner, scanner)) _connectionStatus.Text = "Reader: " + state;
        });
        lock (_sessionGate) { _scanner = scanner; _accepting = true; }
        try
        {
            await scanner.ConnectAsync(local ? null : host, _config.ReaderPort, local, _connectionCancellation.Token);
            if (!scanner.Connected) throw new IOException("The reader closed the connection.");
            _connected = scanner.Connected;
            _connectionStatus.Text = _connected ? "Reader connected. Focus the destination field to receive keyboard actions." : "Reader disconnected. Connect to retry.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await DisconnectAsync();
            _connectionStatus.Text = _closing ? "Disconnected" : "Connection failed: " + ex.Message;
        }
        finally { _connecting = false; SetConnectionControls(); }
    }

    private async Task DisconnectAsync()
    {
        _disconnecting = true;
        SetConnectionControls();
        ScannerConnection? scanner;
        lock (_sessionGate) { _accepting = false; scanner = _scanner; _scanner = null; }
        var cancellation = _connectionCancellation;
        _connectionCancellation = null;
        _connected = false;
        cancellation?.Cancel();
        await _queue.CancelAsync();
        try { if (scanner is not null) await scanner.DisposeAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { _actionStatus.Text = "Reader close: " + ex.Message; }
        cancellation?.Dispose();
        _waitingForTarget = false;
        _disconnecting = false;
        _connectionStatus.Text = "Disconnected. Pending actions cleared.";
        SetConnectionControls();
    }

    private void SetDiscardControl()
    {
        var state = _queue.State;
        _discard.Enabled = !_closing && !_disconnecting && !_discarding
            && (state.ActiveJob is not null || state.PendingCount > 0);
        _discard.Text = _discarding ? "Discarding…" : "Discard Queue";
    }

    private async Task DiscardQueueAsync()
    {
        if (_discarding || _disconnecting || _closing) return;
        _discarding = true;
        SetConnectionControls();
        try
        {
            await _queue.CancelAsync();
            _waitingForTarget = false;
            _actionStatus.Text = "Queue discarded. Ready for new labels.";
        }
        finally
        {
            _discarding = false;
            SetConnectionControls();
            ShowQueueState();
        }
    }

    private void ShowQueueState()
    {
        var state = _queue.State;
        SetDiscardControl();
        _queueStatus.Text = $"Queue: {state.PendingCount}" + (state.ActiveJob is null ? "" : " · running");
        if (state.Error is not null) return;
        if (_waitingForTarget && state.ActiveJob is not null) _actionStatus.Text = "Action waiting. Focus the destination field and release modifier keys.";
        else if (state.ActiveJob is not null) _actionStatus.Text = "Executing " + CommandCatalog.All.Single(c => c.Command == state.ActiveJob.Command).Name + "…";
        else if (_connected) _actionStatus.Text = _dispatcher.SelectedCommand == ActionCommand.Disable
            ? "Disable selected. Raw labels are ignored; command labels remain active." : "Ready for the next label.";
    }

    private async Task CheckReleaseAsync()
    {
        if (_checkingRelease || _args.Contains("--ui-smoke")) return;
        _checkingRelease = true; _nextReleaseCheck = DateTimeOffset.UtcNow.AddMinutes(30);
        try
        {
            var client = new GitHubReleaseClient(_http, _config.GitHubOwner, _config.GitHubRepository, _config.PackagePrefix);
            var release = await client.GetLatestReleaseAsync(_lifetime.Token);
            if (!_closing && release is not null && release.Version.CompareTo(SemanticVersion.Parse(_version)) > 0)
            {
                _update.Text = "Update available";
                _tip.SetToolTip(_update, "Restart from the OEPS shortcut to install the update.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidDataException or ArgumentException)
        { if (!_closing && !IsDisposed) _tip.SetToolTip(_update, "Update check unavailable. The installed app can still run."); }
        finally { _checkingRelease = false; }
    }

    private void Ui(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!_closing && !IsDisposed) action(); }); }
        catch (InvalidOperationException) { }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; _timer.Stop(); _lifetime.Cancel(); SetConnectionControls();
        SaveSettings();
        await _discardOperation;
        await _connectionOperation;
        await DisconnectAsync();
        await _queue.DisposeAsync();
        _canClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _tip.Dispose(); _lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
