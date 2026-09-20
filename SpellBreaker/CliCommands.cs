using System.IO;

namespace SpellBreaker;

/// <summary>
///   -csht  create a SpellBreaker shortcut (.lnk)
/// </summary>
public static class CliCommands
{
    public static bool TryRun(string arg) => arg switch
    {
        "-csht" => Run(CreateShortcut),
        _ => false,
    };

    private static bool Run(Func<bool> fn)
    {
        try { fn(); } catch { }
        return true;
    }

    // ------------------------------------------------------------------ -csht
    private static bool CreateShortcut()
    {
        var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpellBreaker.exe");
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            FileName = "SpellBreaker",
            Filter = "SpellBreaker (*.lnk)|*.lnk",
        };
        if (dlg.ShowDialog() != true) return true;

        var t = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(dlg.FileName);
        sc.TargetPath = exe;
        sc.WorkingDirectory = Path.GetDirectoryName(exe);
        sc.Description = $"{AppInfo.Title} v{AppInfo.Version}";
        sc.IconLocation = exe;
        sc.Save();
        return true;
    }

    /// <summary>Used by the Options page's "create shortcut" checkbox.</summary>
    public static void CreateDesktopShortcut()
    {
        var lnk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SpellBreaker.lnk");
        if (File.Exists(lnk)) return;
        var exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpellBreaker.exe");
        var t = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(lnk);
        sc.TargetPath = exe;
        sc.WorkingDirectory = Path.GetDirectoryName(exe);
        sc.Description = AppInfo.Title;
        sc.IconLocation = exe;
        sc.Save();
    }
}
