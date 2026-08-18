namespace TheWaningBorder.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Two launchers racing each other would fight over the folder swap and
        // could leave a half-installed build behind.
        using var singleton = new Mutex(initiallyOwned: true, "Global\\TheWaningBorder.Launcher", out var owned);

        if (!owned)
        {
            MessageBox.Show(
                "The launcher is already running.",
                "The Waning Border",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }
}
