using Oeps.CommandToAction.Core.Commands;
using Oeps.CommandToAction.Core.Configuration;

namespace Oeps.CommandToAction.App;

internal sealed class ActionSettingsDialog : Form
{
    private readonly CheckBox _salesOrders = new() { Name = "openSalesOrders", Text = "Sales orders (label 4)", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox _deliveries = new() { Name = "openDeliveries", Text = "Outgoing deliveries (label 5)", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown[] _values = Enumerable.Range(0, 5).Select(_ => new NumericUpDown
    {
        Minimum = 0, Maximum = 60000, Increment = 50, ThousandsSeparator = true,
        Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Right
    }).ToArray();

    public ActionTimings Timings => new()
    {
        InitMs = (int)_values[0].Value, SnMs = (int)_values[1].Value,
        TabMs = (int)_values[2].Value, CharMs = (int)_values[3].Value,
        BetweenActionMs = (int)_values[4].Value
    };

    public bool OpenSalesOrders => _salesOrders.Checked;
    public bool OpenDeliveries => _deliveries.Checked;

    public ActionSettingsDialog(UserSettings settings)
    {
        SuspendLayout();
        Text = "Action settings";
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 426);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        var hint = new Label { Text = "Shared timings in milliseconds (1,000 ms = 1 sec).", Dock = DockStyle.Fill, AutoSize = true };
        layout.Controls.Add(hint, 0, 0); layout.SetColumnSpan(hint, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var names = new[] { "INIT — After product", "SN — After serial / batch", "TAB — After send a tab", "CHAT — After send character(s)", "BETWEEN_ACTION — Between jobs" };
        for (var i = 0; i < names.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.Controls.Add(new Label { Text = names[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i + 1);
            _values[i].AccessibleName = names[i];
            layout.Controls.Add(_values[i], 1, i + 1);
        }
        var browserGroup = new GroupBox { Text = "Open in browser", Dock = DockStyle.Fill, Padding = new Padding(8, 3, 8, 5) };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        AddBrowserOption(options, 0, _salesOrders,
            "Scanning label 4 opens the Odoo sales order in your default browser, even when the keyboard command is Disable. " +
            "The link uses sales_order_id with leading zeros removed.\n\n00312 opens\n" + OpenOdooRecordStep.SalesOrder(312).Url.AbsoluteUri);
        AddBrowserOption(options, 1, _deliveries,
            "Scanning label 5 opens the Odoo outgoing delivery in your default browser, even when the keyboard command is Disable. " +
            "The link uses out_id with leading zeros removed.\n\n03854 opens\n" + OpenOdooRecordStep.OutgoingDelivery(3854).Url.AbsoluteUri);
        browserGroup.Controls.Add(options);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        layout.Controls.Add(browserGroup, 0, 6);
        layout.SetColumnSpan(browserGroup, 2);

        var note = new Label { Text = "Changes apply to labels received after saving.", Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(note, 0, 7); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var defaults = new Button { Text = "Defaults", AutoSize = true };
        defaults.Click += (_, _) => SetValues(new UserSettings());
        buttons.Controls.AddRange([save, cancel, defaults]);
        layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        SetValues(settings);
        ResumeLayout(true);
    }

    private void AddBrowserOption(TableLayoutPanel options, int row, CheckBox toggle, string help)
    {
        var info = new Button
        {
            Text = "i", Size = new Size(28, 26), Anchor = AnchorStyles.Left, FlatStyle = FlatStyle.Flat,
            BackColor = Color.White, ForeColor = Color.FromArgb(0, 93, 174),
            AccessibleName = "About " + toggle.Text, AccessibleDescription = help
        };
        info.Click += (_, _) => MessageBox.Show(this, help, toggle.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        options.Controls.Add(toggle, 0, row);
        options.Controls.Add(info, 1, row);
    }

    private void SetValues(UserSettings settings)
    {
        _salesOrders.Checked = settings.OpenSalesOrders;
        _deliveries.Checked = settings.OpenDeliveries;
        var timings = settings.Timings;
        var values = new[] { timings.InitMs, timings.SnMs, timings.TabMs, timings.CharMs, timings.BetweenActionMs };
        for (var i = 0; i < values.Length; i++) _values[i].Value = values[i];
    }
}
