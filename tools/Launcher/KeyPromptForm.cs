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
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, problem is null ? 168 : 196);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Font;

        var y = 18;

        if (problem is not null)
        {
            Controls.Add(new Label
            {
                Text = problem,
                Font = Theme.Body,
                ForeColor = Theme.Danger,
                Location = new Point(20, y),
                Size = new Size(400, 32),
            });
            y += 36;
        }

        Controls.Add(new Label
        {
            Text = "Enter the access key you were given.",
            Font = Theme.Body,
            ForeColor = Theme.Text,
            Location = new Point(20, y),
            Size = new Size(400, 20),
        });

        Controls.Add(new Label
        {
            Text = "It is stored on this machine, so you only do this once.",
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            Location = new Point(20, y + 20),
            Size = new Size(400, 18),
        });

        _input = new TextBox
        {
            Location = new Point(20, y + 46),
            Size = new Size(400, 26),
            Font = new Font("Consolas", 10F),
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_input);

        var ok = new Button
        {
            Text = "Continue",
            DialogResult = DialogResult.OK,
            Location = new Point(240, y + 84),
            Width = 88,
        };
        Theme.Style(ok, primary: true);

        var cancel = new Button
        {
            Text = "Quit",
            DialogResult = DialogResult.Cancel,
            Location = new Point(336, y + 84),
            Width = 84,
        };
        Theme.Style(cancel, primary: false);

        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        // Blocks Continue until something has actually been typed.
        ok.Enabled = false;
        _input.TextChanged += (_, _) => ok.Enabled = Key.Length > 0;
    }
}
