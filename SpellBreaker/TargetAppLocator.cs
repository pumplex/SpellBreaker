using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace SpellBreaker;

public static class TargetAppLocator
{
    public static string TargetAppRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Target);

    public static string TargetAppData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Target, "Local Storage", "leveldb");

    private static readonly string UninstallKey =
        $@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppInfo.Target}";

    /// <summary>A dir is a target application install root if it has app-* version dirs or resources\app.asar.</summary>
    public static bool IsValidRoot(string dir) =>
        !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) &&
        (Directory.GetDirectories(dir, "app-*").Length > 0 ||
         File.Exists(Path.Combine(dir, "resources", "app.asar")));

    /// <summary>
    /// Locate the target application install root ->
    /// registry Uninstall Target Application Install Location -> Epic Games folders -> null.
    /// </summary>
    public static string? FindInstallRoot()
    {
        var saved = App.Opts.TargetAppFolder;
        if (IsValidRoot(saved)) return saved;

        if (IsValidRoot(TargetAppRoot)) return TargetAppRoot;

        try
        {
            var reg = Registry.GetValue(UninstallKey, "InstallLocation", null) as string;
            if (reg != null && IsValidRoot(reg)) return reg;
        }
        catch { }

        foreach (var pf in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        })
        {
            var epic = Path.Combine(pf, "Epic Games", AppInfo.Target);
            if (IsValidRoot(epic)) return epic;
        }
        return null;
    }

    /// <summary>Folder-picker fallback: validate the chosen dir, persist it to Options on success.</summary>
    public static string? PromptForTargetAppFolder(Window? owner)
    {
        var dlg = new OpenFolderDialog { Title = App.Lang["LANG71"] };
        if (dlg.ShowDialog(owner) != true) return null;
        if (!IsValidRoot(dlg.FolderName)) return null;
        App.Opts.TargetAppFolder = dlg.FolderName;
        try { App.Opts.Save(); } catch { }
        return dlg.FolderName;
    }

    public static bool IsRunning() => TargetAppProcesses().Count > 0;

    private static List<Process> TargetAppProcesses() =>
        new(Process.GetProcessesByName(AppInfo.Target));

    /// <summary>
    /// Interactive running-check: offer to close the target application, kill if accepted, recheck if declined.
    /// Returns false when the target application is still running afterwards.
    /// </summary>
    public static bool EnsureTargetAppClosedInteractive(Window? owner)
    {
        if (!IsRunning()) return true;
        var L = App.Lang;
        var answer = MessageBox.Show(owner, L["LANG69"], $"{AppInfo.Title} v{AppInfo.Version}",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
        {
            foreach (var p in TargetAppProcesses())
                try { p.CloseMainWindow(); } catch { }
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(4) && IsRunning()) Thread.Sleep(150);
            foreach (var p in TargetAppProcesses())
                try { p.Kill(); } catch { }
            Thread.Sleep(300);
        }
        // declined, or kill failed: recheck — the user may have closed it meanwhile
        return !IsRunning();
    }

    /// <summary>All app-* version contexts under <paramref name="root"/>, newest first.</summary>
    public static List<TargetAppContext> ListInstalls(string root)
    {
        var list = new List<TargetAppContext>();
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root, "app-*"))
        {
            var name = Path.GetFileName(dir)[4..];
            if (!Version.TryParse(name, out var v)) continue;
            var resources = Path.Combine(dir, "resources");
            if (!Directory.Exists(resources)) continue;
            list.Add(BuildContext(resources, TargetAppData, v.ToString()));
        }
        // a picked folder may contain resources\ directly (no app-* wrapper)
        if (list.Count == 0 && File.Exists(Path.Combine(root, "resources", "app.asar")))
            list.Add(BuildContext(Path.Combine(root, "resources"), TargetAppData, ""));
        return list
            .OrderByDescending(c => Version.TryParse(c.LastVersion, out var v) ? v : new Version(0, 0))
            .ToList();
    }

    /// <summary>Build a TargetAppContext for the latest installed version under <paramref name="root"/>.</summary>
    public static TargetAppContext? DetectLatest(string? root = null)
        => ListInstalls(root ?? FindInstallRoot() ?? TargetAppRoot).FirstOrDefault();

    /// <summary>Build a TargetAppContext from a user-picked asar file (legacy custom-dir flow).</summary>
    public static TargetAppContext BuildCustomContext(string asarFile)
    {
        var dir = Path.GetDirectoryName(asarFile)!;
        var data = dir.Replace($@"App\{AppInfo.Target}\resources", $@"Data\{AppInfo.Target}\Local Storage\leveldb");
        return BuildContext(dir, data, "");
    }

    private static TargetAppContext BuildContext(string resources, string data, string lastVersion)
    {
        var ctx = new TargetAppContext
        {
            ResourcesPath = resources,
            TargetAppExePath = Path.Combine(Directory.GetParent(resources)!.FullName, AppInfo.Target + ".exe"),
            TargetAppDataPath = data,
            LastVersion = lastVersion,
        };
        // AppAsarVersion is filled lazily by ModInspector.Inspect (async version scan)
        return ctx;
    }

    /// <summary>Version string embedded in asar file ("version":"x.y.z"), like the batch regex.</summary>
    public static string ReadAsarVersion(string asarPath)
    {
        try
        {
            using var fs = File.OpenRead(asarPath);
            var re = new Regex("\"version\":\"(\\d{1,2}\\.\\d{1,2}\\.\\d{1,2})\"");
            var tail = "";
            var chunk = new char[4 * 1024 * 1024];
            int n;
            using var reader = new StreamReader(fs, Encoding.Latin1);
            while ((n = reader.Read(chunk, 0, chunk.Length)) > 0)
            {
                var text = tail + new string(chunk, 0, n);
                var m = re.Match(text);
                if (m.Success) return m.Groups[1].Value;
                tail = text.Length > 64 ? text[^64..] : text;
            }
            return "";
        }
        catch { return ""; }
    }

    /// <summary>Result of the startup/Continue detection pass.</summary>
    public record StartupDetection(
        TargetAppContext? Ctx, string? Error, string Username,
        List<TargetAppContext> Installs, string? InstallRoot);

    /// <summary>
    /// Full detection pass with progress reporting: locate install -> running note ->
    /// resolve versions -> extract username. `error` is "install" | "asar" | "running" | null.
    /// </summary>
    public static StartupDetection DetectStartup(Action<int, string> report)
    {
        report(15, $"Loading {AppInfo.Title}...");

        report(35, "Locating target application install...");
        var root = FindInstallRoot();
        if (root == null)
            return new(null, "install", "", new List<TargetAppContext>(), null);

        report(55, "Checking if target application is running...");
        var running = IsRunning();

        report(75, "Reading asar file...");
        var installs = ListInstalls(root);
        if (installs.Count == 0)
            return new(null, "install", "", installs, root);
        var ctx = installs[0];
        var asarPath = Path.Combine(ctx.ResourcesPath, "app.asar");
        if (!File.Exists(asarPath)) return new(ctx, "asar", "", installs, root);

        report(90, "Loading account data...");
        var username = ExtractUsername(ctx.TargetAppDataPath);
        Thread.Sleep(150); // let the last status paint
        report(100, "OK");
        return new(ctx, running ? "running" : null, username, installs, root);
    }

    /// <summary>Login username from the newest leveldb *.log</summary>
    public static string ExtractUsername(string leveldbPath)
    {
        try
        {
            if (!Directory.Exists(leveldbPath)) return "";
            var logs = Directory.GetFiles(leveldbPath, "*.log")
                .OrderBy(f => new FileInfo(f).LastWriteTimeUtc).ToList();
            var username = "";
            foreach (var f in logs)
            {
                var text = File.ReadAllText(f, Encoding.Latin1)
                    .Replace("\0", "").Replace('"', ' ').Replace('}', ' ');
                foreach (var part in text.Split(','))
                {
                    var idx = part.IndexOf("username:", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                        username = part[(idx + 9)..].Trim();
                }
            }
            return username;
        }
        catch { return ""; }
    }
}
