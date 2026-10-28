using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.App;

internal sealed class TimingDialog : Form
{
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

    public TimingDialog(ActionTimings timings)
    {
        Text = "Action timing";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 324);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 8 };
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
        var note = new Label { Text = "Changes apply to labels received after saving.", Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = true };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(note, 0, 6); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var defaults = new Button { Text = "Defaults", AutoSize = true };
        defaults.Click += (_, _) => SetValues(new());
        buttons.Controls.AddRange([save, cancel, defaults]);
        layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        SetValues(timings);
    }

    private void SetValues(ActionTimings timings)
    {
        var values = new[] { timings.InitMs, timings.SnMs, timings.TabMs, timings.CharMs, timings.BetweenActionMs };
        for (var i = 0; i < values.Length; i++) _values[i].Value = values[i];
    }
}
