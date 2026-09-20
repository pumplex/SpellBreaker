using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace SpellBreaker;

public partial class MainWindow : Window
{
    private readonly TargetAppLocator.StartupDetection? _detection;
    private List<TargetAppContext> _installs = new();
    private string _username = "";
    private SelectorView? _selector;
    private OptionsView? _options;
    private string? _logPath;
    private DispatcherTimer? _returnTimer;
    private int _returnCountdown;

    public MainWindow(TargetAppLocator.StartupDetection? detection = null)
    {
        _detection = detection;
        InitializeComponent();
        RefreshLabels();
        ShowPage(LauncherPanel);
    }

    private void RefreshLabels()
    {
        var L = App.Lang;
        Title = $"{AppInfo.WindowTitle} v{AppInfo.Version}";
        AppTitle.Text = $"{AppInfo.Title} v{AppInfo.Version}";
        SysLangLabel.Text = $"@{L["LANG_system"]}";
        SysLangValue.Text = $"{CultureInfo.CurrentCulture.DisplayName} [{CultureInfo.CurrentCulture.Name}]";
        AppLangLabel.Text = $"@{L["LANG02"]}";
        AppLangValue.Text = $"{L.Language} [{L.BatchLang}]";
        ContinueButton.Content = $"{L["LANG58"]}\n{AppInfo.Title}";
        CancelButton.Content = $"{L["LANG63"]}\n{AppInfo.Title}";
        OptionsButton.Content = L["LANG60"];
    }

    private void ShowPage(UIElement page)
    {
        LauncherPanel.Visibility = Visibility.Collapsed;
        SelectorPanel.Visibility = Visibility.Collapsed;
        OptionsPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Close();

    private void OptionsClicked(object sender, RoutedEventArgs e)
    {
        _options = new OptionsView();
        _options.BackRequested += () => { RefreshLabels(); ShowPage(LauncherPanel); };
        _options.Saved += RefreshLabels;
        OptionsPanel.Child = _options;
        ShowPage(OptionsPanel);
    }

    // ================================================================== CONTINUE
    private void ContinueClicked(object sender, RoutedEventArgs e)
    {
        var L = App.Lang;

        // running check: offer to close, recheck, give up only if still running
        if (!TargetAppLocator.EnsureTargetAppClosedInteractive(this)) { Close(); return; }

        // resolve installs: reuse startup detection, else rescan
        _installs = _detection?.Installs ?? new List<TargetAppContext>();
        if (_installs.Count == 0)
            _installs = TargetAppLocator.ListInstalls(TargetAppLocator.FindInstallRoot() ?? TargetAppLocator.TargetAppRoot);

        if (_installs.Count == 0)
        {
            // folder-picker fallback, then advise to install the target application
            var picked = TargetAppLocator.PromptForTargetAppFolder(this);
            if (picked != null) _installs = TargetAppLocator.ListInstalls(picked);
            if (_installs.Count == 0)
            {
                MessageBox.Show(L["LANG72"], $"{AppInfo.Title} v{AppInfo.Version}",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close(); return;
            }
        }

        _username = _detection?.Username ?? "";
        if (string.IsNullOrEmpty(_username))
            _username = TargetAppLocator.ExtractUsername(_installs[0].TargetAppDataPath);

        ShowSelector();
    }

    private void ShowSelector()
    {
        _selector = new SelectorView(_installs, _username);
        _selector.ModifyRequested += OnModifyRequested;
        _selector.RestoreRequested += OnRestoreRequested;
        _selector.BackRequested += () => ShowPage(LauncherPanel);
        SelectorPanel.Child = _selector;
        ShowPage(SelectorPanel);
    }

    // ================================================================== MODIFY
    private async void OnModifyRequested(SelectorResult sel, TargetAppContext ctx)
    {
        if (!TargetAppLocator.EnsureTargetAppClosedInteractive(this)) return; // stay on selector

        BeginProgress($"{AppInfo.Title} v{AppInfo.Version}  |  {AppInfo.Target}" +
            (string.IsNullOrEmpty(ctx.LastVersion) ? "" : $" v{ctx.LastVersion}") +
            (string.IsNullOrEmpty(ctx.AppAsarVersion) ? "" : $"  |  app.asar v{ctx.AppAsarVersion}"));
        PreLog(sel, ctx);

        var modifier = new TargetAppModifier(App.Lang, ctx, sel)
        {
            StatusText = t => Dispatcher.Invoke(() => StatusLine.Text = t),
            LogLine = (t, k) => Dispatcher.Invoke(() => AddLog(t, k)),
            ProgressChanged = p => Dispatcher.Invoke(() => Progress.Value = p),
        };

        await Task.Run(() => modifier.Run());
        SoundPlayer.Play(modifier.Failed ? SoundKind.Failure : SoundKind.Success);
        FinishProgress();
    }

    // ================================================================== RESTORE
    private async void OnRestoreRequested(VersionEntry entry)
    {
        var L = App.Lang;
        if (!TargetAppLocator.EnsureTargetAppClosedInteractive(this)) return; // stay on selector

        var yes = MessageBox.Show(L["LANG74"], $"{AppInfo.Title} v{AppInfo.Version}",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!yes) return;

        BeginProgress($"{AppInfo.Title} v{AppInfo.Version}  |  {L["LANG73"]}  |  {AppInfo.Target} v{entry.Status?.Version}");

        var restorer = new TargetAppRestorer(App.Lang, entry.Ctx)
        {
            StatusText = t => Dispatcher.Invoke(() => StatusLine.Text = t),
            LogLine = (t, k) => Dispatcher.Invoke(() => AddLog(t, k)),
            ProgressChanged = p => Dispatcher.Invoke(() => Progress.Value = p),
        };

        await Task.Run(() => restorer.Restore());
        SoundPlayer.Play(restorer.Failed ? SoundKind.Failure : SoundKind.Success);
        FinishProgress();
    }

    // ================================================================== PROGRESS PAGE
    private void BeginProgress(string title)
    {
        CancelReturn();
        ProgressBackButton.Visibility = Visibility.Collapsed;
        CountdownText.Visibility = Visibility.Collapsed;
        LogList.Items.Clear();
        Progress.Value = 0;
        StatusLine.Text = "";
        ModifyTitle.Text = title;
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SpellBreaker.log");
        try { File.WriteAllText(_logPath, $"# {title} — {DateTime.Now:u}\n\n"); } catch { _logPath = null; }
        ShowPage(ProgressPanel);
    }

    private void FinishProgress()
    {
        ProgressBackButton.Content = App.Lang["LANG65"];
        ProgressBackButton.Visibility = Visibility.Visible;
        _returnCountdown = 10;
        UpdateCountdownText();
        CountdownText.Visibility = Visibility.Visible;
        _returnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _returnTimer.Tick += OnReturnTick;
        _returnTimer.Start();
    }

    private void OnReturnTick(object? sender, EventArgs e)
    {
        if (--_returnCountdown <= 0) { ReturnToSelector(); return; }
        UpdateCountdownText();
    }

    private void UpdateCountdownText() =>
        CountdownText.Text = string.Format(App.Lang["LANG77"], _returnCountdown);

    private void ReturnToSelector()
    {
        CancelReturn();
        _selector?.RefreshVersions();
        ShowPage(SelectorPanel);
    }

    private void ProgressBackClicked(object sender, RoutedEventArgs e) => ReturnToSelector();

    private void ProgressPanelClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_returnTimer?.IsEnabled != true) return;
        for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
            if (d == ProgressBackButton) return;   // let the button's Click fire normally
        CancelReturn();
        CountdownText.Visibility = Visibility.Collapsed;
    }

    private void CancelReturn()
    {
        _returnTimer?.Stop();
        _returnTimer = null;
    }

    private void FooterLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        CancelReturn();   // footer click counts as a page click — stop the auto-return countdown
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void PreLog(SelectorResult r, TargetAppContext ctx)
    {
        var L = App.Lang;
        AddLog($" @{L["LANG02"]}: {L.Language} [{L.BatchLang}]", LogKind.Info);
        var versions = _installs.Select(i => i.LastVersion).Where(v => !string.IsNullOrEmpty(v)).ToList();
        if (versions.Count > 0)
            AddLog($" {L["LANG13"]}: | v{string.Join(" | v", versions)} |", LogKind.Info);
        var newest = _installs
            .Select(i => i.LastVersion)
            .Where(v => !string.IsNullOrEmpty(v))
            .OrderByDescending(v => Version.TryParse(v, out var p) ? p : new Version(0, 0))
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(newest))
            AddLog($" {L["LANG14"]}: {AppInfo.Target} v{newest}", LogKind.Ok);
        if (!string.IsNullOrEmpty(ctx.AppAsarVersion))
            AddLog($" app.asar v{ctx.AppAsarVersion}", LogKind.Ok);

        AddLog("", LogKind.Info);
        AddLog($" {L["LANG15"]}", LogKind.Title);
        AddLog(r.EnablePro
            ? $" {(r.ProMethod == "Adaptive" ? L["LANG19"] : L["LANG20"] + " v" + r.SakVersion)}"
            : $" {L["LANG17"]}", LogKind.Info);
        AddLog($" {L["LANG23"]}", LogKind.Title);
        AddLog(r.DisableAutoUpdates ? $" {L["LANG25"]}" : $" {L["LANG26"]}", LogKind.Info);
        AddLog($" {L["LANG27"]}", LogKind.Title);
        AddLog(r.EnableDevtools ? $" {L["LANG29"]}" : $" {L["LANG30"]}", LogKind.Info);
        AddLog("", LogKind.Info);
    }

    private void AddLog(string text, LogKind kind)
    {
        if (_logPath != null)
            try { File.AppendAllText(_logPath, text + "\n"); } catch { }

        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        tb.Foreground = kind switch
        {
            LogKind.Ok => (Brush)FindResource("OkFg"),
            LogKind.Fail => (Brush)FindResource("FailFg"),
            LogKind.Title => (Brush)FindResource("Legend"),
            _ => (Brush)FindResource("Fg"),
        };
        if (kind == LogKind.Title) tb.FontWeight = FontWeights.Bold;
        LogList.Items.Add(tb);
        LogScroll.ScrollToEnd();
    }
}
