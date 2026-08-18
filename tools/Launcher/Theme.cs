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

    public static void Style(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(70, 66, 80);
        button.BackColor = primary ? Color.FromArgb(58, 50, 34) : Panel;
        button.ForeColor = primary ? Accent : Text;
        button.Font = Body;
        button.Height = 30;
        button.Cursor = Cursors.Hand;
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
