using Oeps.CommandToAction.Core.Commands;

namespace Oeps.CommandToAction.App;

internal sealed class CommandInfoDialog : Form
{
    public CommandInfoDialog(ActionCommand command)
    {
        Text = CommandCatalog.Get(command).Name;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 350);
        MinimumSize = new Size(600, 350);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Padding = new Padding(20);
        var text = new TextBox
        {
            Text = CommandCatalog.GetHelpText(command).ReplaceLineEndings(),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White,
            Font = new Font("Consolas", 10), AccessibleName = "Command description and sequence",
            TabStop = false
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(close);
        Controls.Add(text); Controls.Add(buttons);
        AcceptButton = CancelButton = close;
        Shown += (_, _) => { text.SelectionLength = 0; close.Focus(); };
    }
}
