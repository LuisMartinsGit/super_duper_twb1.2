using System.Drawing;

namespace TheWaningBorder.Launcher;

/// <summary>Shown on first run, and again if the server rejects a stored key.</summary>
internal sealed class KeyPromptForm : Form
{
    private readonly TextBox _input;

    public string Key => _input.Text.Trim();

    public KeyPromptForm(string? problem = null)
    {
        Text = "The Waning Border";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;

        // Font before AutoScaleMode: WinForms scales against the form font, so
        // setting it afterwards gives the layout the wrong baseline.
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Font;

        // The dialog takes its size from its contents rather than a hardcoded
        // ClientSize, so nothing clips when the display is scaled.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(22);

        // FlowLayoutPanel, undocked, positioned at the padding origin. A
        // docked panel takes its width FROM the form, so the form had nothing
        // to auto-size against and collapsed to 258px around 440px of content.
        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(22, 22),
            BackColor = Color.Transparent,
        };

        if (problem is not null)
        {
            var warning = Theme.Label(problem, Theme.Body, Theme.Danger);
            warning.Margin = new Padding(0, 0, 0, 14);
            layout.Controls.Add(warning);
        }

        layout.Controls.Add(Theme.Label(
            "Enter the access key you were given.", Theme.Body, Theme.Text));

        var hint = Theme.Label(
            "It is stored on this machine, so you only do this once.",
            Theme.Small, Theme.Muted);
        hint.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(hint);

        _input = new TextBox
        {
            Width = Theme.ContentWidth,
            Font = new Font("Consolas", 10.5F),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 4),
        };
        layout.Controls.Add(_input);

        var ok = new Button { Text = "Continue", DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = "Quit", DialogResult = DialogResult.Cancel };

        Theme.Style(ok, primary: true);
        Theme.Style(cancel, primary: false);

        layout.Controls.Add(Theme.ButtonRow(ok, cancel));
        Controls.Add(layout);

        AcceptButton = ok;
        CancelButton = cancel;

        _input.TextChanged += (_, _) => ok.Enabled = Key.Length > 0;
        Shown += (_, _) => _input.Focus();
    }
}
