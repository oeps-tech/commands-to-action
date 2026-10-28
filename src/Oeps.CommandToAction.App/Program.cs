using Oeps.CommandToAction.Core.Configuration;
using Oeps.CommandToAction.Core.Updates;

namespace Oeps.CommandToAction.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var index = Array.IndexOf(args, "--data-dir");
            var paths = new AppPaths(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            var configuration = AppConfiguration.Load(AppContext.BaseDirectory, paths.UserDataRoot);
            var smoke = args.Contains("--ui-smoke");
            MainForm? form = null;
            using var instance = smoke ? null : AppInstanceCoordinator.TryAcquire(() =>
            {
                if (form is not { IsHandleCreated: true, IsDisposed: false }) return;
                try { form.BeginInvoke(() => { form.WindowState = FormWindowState.Normal; form.Show(); form.Activate(); }); }
                catch (InvalidOperationException) { }
            });
            if (instance is null && !smoke) return;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var main = new MainForm(configuration, paths, http, args);
            form = main;
            Application.Run(main);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            if (args.Contains("--ui-smoke")) throw;
            MessageBox.Show("OEPS Command to Action could not start.\n\n" + ex.Message,
                "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
