namespace TheWaningBorder.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Two launchers racing each other would fight over the folder swap and
        // could leave a half-installed build behind.
        // One mutex per edition: each owns its own install root, so a demo and
        // a developer launcher open together share no folder to fight over.
        // The demo keeps the original name so it still excludes old launchers.
        var mutexName = Edition.IsDemo
            ? "Global\\TheWaningBorder.Launcher"
            : $"Global\\TheWaningBorder.Launcher.{Edition.Id}";
        using var singleton = new Mutex(initiallyOwned: true, mutexName, out var owned);

        if (!owned)
        {
            MessageBox.Show(
                "The launcher is already running.",
                Edition.DisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }
}
