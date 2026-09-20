using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace SpellBreaker;

public partial class App : Application
{
    public static Options Opts = null!;
    public static Localization Lang = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && CliCommands.TryRun(e.Args[0]))
        {
            Shutdown();
            return;
        }

        // relaunch elevated when the app dir isn't writable
        if (!IsElevated() && TryRelaunchElevated())
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;

        ReloadSettings();

        // startup splash: run detection with progress while the main GUI stays hidden
        var loader = new LoadingWindow();
        loader.Show();
        var detection = await Task.Run(() => TargetAppLocator.DetectStartup(
            (p, s) => loader.Dispatcher.Invoke(() => loader.Report(p, s))));

        // show the launcher before closing the splash — closing the last window shuts the app down
        var w = new MainWindow(detection);
        w.Show();
        loader.Close();
    }

    private void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpellBreaker.log"),
                $"[{DateTime.Now:u}] UNHANDLED: {e.Exception}\n\n");
        }
        catch { }
        MessageBox.Show($"{AppInfo.Title} {AppInfo.Version}\n\n{e.Exception.Message}",
            AppInfo.Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ReloadSettings()
    {
        Opts = Options.Load();
        Lang = Localization.Load(Opts.ForceLanguage);
        ThemeManager.Apply(Opts.EffectiveTheme());
    }

    private static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRelaunchElevated()
    {
        try
        {
            var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpellBreaker.exe");
            if (!File.Exists(exe))
            {
                var pp = Environment.ProcessPath;
                if (pp != null && Path.GetFileName(pp).Equals("SpellBreaker.exe", StringComparison.OrdinalIgnoreCase))
                    exe = pp;
                else return false; // e.g. `dotnet SpellBreaker.dll` dev runs — nothing sensible to elevate
            }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch { return false; }
    }
}
