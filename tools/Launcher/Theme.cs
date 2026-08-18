using System.Drawing;

namespace TheWaningBorder.Launcher;

/// <summary>Shared palette and formatting so the two forms match.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 22, 28);
    public static readonly Color Panel = Color.FromArgb(34, 31, 40);
    public static readonly Color Text = Color.FromArgb(228, 224, 234);
    public static readonly Color Muted = Color.FromArgb(150, 144, 162);
    public static readonly Color Accent = Color.FromArgb(196, 162, 92);
    public static readonly Color Danger = Color.FromArgb(214, 118, 106);

    public static readonly Font Title = new("Segoe UI Semibold", 15F);
    public static readonly Font Body = new("Segoe UI", 9.75F);
    public static readonly Font Small = new("Segoe UI", 8.5F);

    /// <summary>Width the text columns wrap at, in base 96-DPI units.</summary>
    public const int ContentWidth = 440;

    /// <summary>
    /// A label that grows to fit its text instead of clipping it.
    ///
    /// Every label here is AutoSize with a MaximumSize width: fixing a label's
    /// height in pixels works only at 100% scaling, and clips the moment the
    /// font gets bigger.
    /// </summary>
    public static Label Label(string text, Font font, Color color, int maxWidth = ContentWidth) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = true,
        MaximumSize = new Size(maxWidth, 0),
        Margin = new Padding(0, 0, 0, 6),
    };

    public static void Style(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(70, 66, 80);
        button.BackColor = primary ? Color.FromArgb(58, 50, 34) : Panel;
        button.ForeColor = primary ? Accent : Text;
        button.Font = Body;
        button.Cursor = Cursors.Hand;

        // Sized by its own text plus padding rather than a fixed Height, for
        // the same reason the labels are.
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.Padding = new Padding(18, 7, 18, 7);
        button.Margin = new Padding(8, 0, 0, 0);
    }

    /// <summary>A right-aligned row of buttons, laid out in the order given.</summary>
    public static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
            BackColor = Color.Transparent,
        };

        // RightToLeft flow lays the first control out furthest right, so the
        // array reads left-to-right on screen when reversed here.
        for (int i = buttons.Length - 1; i >= 0; i--) row.Controls.Add(buttons[i]);

        return row;
    }

    public static string Bytes(long value)
    {
        if (value >= 1L << 30) return $"{value / (double)(1L << 30):0.0} GB";
        if (value >= 1L << 20) return $"{value / (double)(1L << 20):0} MB";
        if (value >= 1L << 10) return $"{value / (double)(1L << 10):0} KB";
        return $"{value} B";
    }

    public static string Duration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return $"{(int)value.TotalHours}h {value.Minutes}m left";
        if (value.TotalMinutes >= 1) return $"{(int)value.TotalMinutes}m {value.Seconds}s left";
        return $"{Math.Max(1, (int)value.TotalSeconds)}s left";
    }
}
