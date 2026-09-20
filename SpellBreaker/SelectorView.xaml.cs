using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SpellBreaker;

/// <summary>One installed target application version + its detected modification state, shown in the version list.</summary>
public class VersionEntry
{
    public required TargetAppContext Ctx { get; init; }
    public VersionStatus? Status { get; set; }
    public bool Scanned;
    public bool ScanFailed;
    public CheckBox? Box;
    public ProgressBar? Bar;
    public ContentControl? Host;
}

/// <summary>
/// Selector page: TARGET APPLICATION VERSION list (checkbox + modification tags + per-version Restore),
/// then the PRO / AUTO-UPDATE / DEVTOOLS groups, then BACK / MODIFY.
/// Version modification-state scans run asynchronously, newest first, with a per-row
/// progress bar that fades into the detected status.
/// </summary>
public partial class SelectorView : UserControl
{
    public event Action<SelectorResult, TargetAppContext>? ModifyRequested;
    public event Action<VersionEntry>? RestoreRequested;
    public event Action? BackRequested;

    private readonly List<VersionEntry> _versions = new();
    private CancellationTokenSource? _scanCts;

    public SelectorView(List<TargetAppContext> installs, string detectedUsername)
    {
        InitializeComponent();
        var L = App.Lang;

        VerHeader.Text = L["LANG66"];
        ProHeader.Text = L["LANG15"];
        ProQuestion.Text = L["LANG16"];
        MethodLabel.Text = L["LANG18"];
        UsernameCheck.Content = L["LANG62"];
        UpdHeader.Text = L["LANG23"];
        UpdQuestion.Text = L["LANG24"];
        UpdCheck.Content = L["LANG25"];
        DevHeader.Text = L["LANG27"];
        DevQuestion.Text = L["LANG28"];
        DevCheck.Content = L["LANG29"];
        BackButton.Content = L["LANG65"];
        ModifyButton.Content = L["LANG64"];

        // defaults AFTER InitializeComponent (event handlers fire on these setters)
        var opt = App.Opts;
        ProCheck.IsChecked = true;
        SakItem.Content = AppInfo.SakMethod;
        SelectMethod(opt.LastMethod == "Adaptive" ? "Adaptive" : "Sak");
        if (opt.LastSakVersion == "1.0.4") Sak104.IsChecked = true; else Sak107.IsChecked = true;
        UsernameBox.Text = !string.IsNullOrWhiteSpace(opt.LastUsername) ? opt.LastUsername
            : !string.IsNullOrWhiteSpace(detectedUsername) ? detectedUsername : "Custom Username";
        UsernameCheck.IsChecked = opt.LastUsernameOn;
        UpdCheck.IsChecked = opt.LastNoUpdate;
        DevCheck.IsChecked = opt.LastDevtools;

        foreach (var ctx in installs)
            _versions.Add(new VersionEntry { Ctx = ctx });

        MethodChanged(null, null);
        OptionToggled(null, null);
        Unloaded += (_, _) => _scanCts?.Cancel();
        RefreshVersions();
    }

    // ================================================================ version rows
    /// <summary>Rebuild the version rows and rescan each version asynchronously.</summary>
    public void RefreshVersions()
    {
        BuildRows();
        UpdateModifyButton();
        _ = ScanAllAsync();
    }

    private void BuildRows()
    {
        VersionList.Children.Clear();
        foreach (var e in _versions)
        {
            e.Scanned = false;
            e.ScanFailed = false;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

            var box = new CheckBox { IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            var entry = e;
            box.Checked += (_, _) =>
            {
                foreach (var other in _versions)
                    if (!ReferenceEquals(other, entry) && other.Box != null) other.Box.IsChecked = false;
                UpdateModifyButton();
            };
            box.Unchecked += (_, _) => UpdateModifyButton();
            e.Box = box;
            row.Children.Add(box);

            row.Children.Add(new TextBlock
            {
                Text = $"{AppInfo.Target} {e.Ctx.LastVersion}",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            });

            var bar = new ProgressBar
            {
                Width = 150,
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = (Brush)FindResource("Accent"),
                Background = (Brush)FindResource("InputBg"),
            };
            e.Bar = bar;
            var host = new ContentControl { Content = bar, VerticalAlignment = VerticalAlignment.Center };
            e.Host = host;
            row.Children.Add(host);

            VersionList.Children.Add(row);
        }
    }

    /// <summary>Scan versions sequentially (newest first), revealing each status as it completes.</summary>
    private async Task ScanAllAsync()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        foreach (var e in _versions)
        {
            if (cts.IsCancellationRequested) return;
            var entry = e;
            VersionStatus st;
            try
            {
                st = await Task.Run(() => ModInspector.Inspect(entry.Ctx,
                    p => { try { Dispatcher.BeginInvoke(() => { if (entry.Bar != null) entry.Bar.Value = p; }); } catch { } }));
            }
            catch
            {
                st = new VersionStatus { Version = entry.Ctx.LastVersion };
                entry.ScanFailed = true;
            }
            if (cts.IsCancellationRequested) return;
            entry.Status = st;
            entry.Scanned = true;
            await RevealStatusAsync(entry, cts.Token);
            UpdateModifyButton();
        }
    }

    /// <summary>Fade the scan bar out, swap in the detected status, fade it in.</summary>
    private async Task RevealStatusAsync(VersionEntry e, CancellationToken ct)
    {
        if (e.Host == null || e.Bar == null) return;
        await FadeAsync(e.Bar, 1, 0, 120);
        if (ct.IsCancellationRequested) return;

        UIElement content;
        if (e.ScanFailed)
        {
            content = DimText("(scan failed)");
        }
        else if (e.Status is { ModifiedAny: true } st)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var tag in st.Tags()) panel.Children.Add(TagBlock(tag));
            var restore = new Button
            {
                Content = App.Lang["LANG68"],
                Style = (Style)FindResource("ThemedButton"),
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 11,
            };
            var entry = e;
            restore.Click += (_, _) => RestoreRequested?.Invoke(entry);
            panel.Children.Add(restore);
            content = panel;
            if (e.Box != null) { e.Box.IsEnabled = false; e.Box.IsChecked = false; }
        }
        else
        {
            content = DimText($" ({App.Lang["LANG67"]})");
            if (e.Box != null)
            {
                e.Box.IsEnabled = true;
                if (_versions.All(v => v.Box?.IsChecked != true)) e.Box.IsChecked = true;
            }
        }

        content.Opacity = 0;
        e.Host.Content = content;
        await FadeAsync(content, 0, 1, 160);
    }

    private TextBlock DimText(string text) => new()
    {
        Text = text,
        Foreground = (Brush)FindResource("DimFg"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };

    private static Task FadeAsync(UIElement el, double from, double to, int ms)
    {
        var tcs = new TaskCompletionSource();
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
        anim.Completed += (_, _) => tcs.TrySetResult();
        el.BeginAnimation(UIElement.OpacityProperty, anim);
        return tcs.Task;
    }

    private static Border TagBlock(string tag)
    {
        var color = tag.StartsWith(AppInfo.ProLabel) ? "#6B4FD8"
            : tag == "Promos" ? "#0E8A8A"
            : tag == "Overlay" ? "#C77414"
            : "#4A6E8C";
        return new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = tag, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold },
        };
    }

    // ================================================================ options
    private void UpdateModifyButton()
    {
        var anyVersion = _versions.Any(v => v.Box?.IsChecked == true);
        var anyOption = ProCheck.IsChecked == true || UpdCheck.IsChecked == true || DevCheck.IsChecked == true;
        ModifyButton.IsEnabled = anyVersion && anyOption;
    }

    private void SelectMethod(string tag)
    {
        foreach (ComboBoxItem i in MethodCombo.Items)
            if ((i.Tag as string) == tag) { MethodCombo.SelectedItem = i; return; }
        MethodCombo.SelectedIndex = 0;
    }

    private void MethodChanged(object? sender, SelectionChangedEventArgs? e)
    {
        if (SakVersionPanel == null || ProCheckLabel == null) return; // mid-InitializeComponent
        var sak = (MethodCombo.SelectedItem as ComboBoxItem)?.Tag as string != "Adaptive";
        SakVersionPanel.Visibility = sak ? Visibility.Visible : Visibility.Collapsed;
        ProCheckLabel.Text = App.Lang[sak ? "LANG20" : "LANG19"];
    }

    private void OptionToggled(object? sender, RoutedEventArgs? e)
    {
        if (UsernameCheck == null) return; // mid-InitializeComponent
        var on = ProCheck.IsChecked == true;
        UsernameCheck.IsEnabled = on;
        if (!on) UsernameCheck.IsChecked = false;
        UpdateModifyButton();
    }

    private void BackClicked(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        BackRequested?.Invoke();
    }

    private void ModifyClicked(object sender, RoutedEventArgs e)
    {
        var entry = _versions.FirstOrDefault(v => v.Box?.IsChecked == true);
        if (entry == null) return;
        _scanCts?.Cancel();

        var result = new SelectorResult
        {
            EnablePro = ProCheck.IsChecked == true,
            ProMethod = ((MethodCombo.SelectedItem as ComboBoxItem)?.Tag as string) == "Adaptive"
                ? "Adaptive" : AppInfo.SakMethod,
            SakVersion = Sak104.IsChecked == true ? "1.0.4" : "1.0.7",
            DisableAutoUpdates = UpdCheck.IsChecked == true,
            EnableDevtools = DevCheck.IsChecked == true,
        };
        result.ChangeUsername = result.EnablePro && UsernameCheck.IsChecked == true;
        result.CustomUsername = UsernameBox.Text.Trim();
        result.ModPromos = App.Opts.ModPromos;
        result.ModOverlay = App.Opts.ModOverlay;

        // persist choices for next run
        var opt = App.Opts;
        opt.LastMethod = result.ProMethod;
        opt.LastSakVersion = result.SakVersion;
        opt.LastUsernameOn = result.ChangeUsername;
        opt.LastUsername = result.CustomUsername;
        opt.LastNoUpdate = result.DisableAutoUpdates;
        opt.LastDevtools = result.EnableDevtools;
        try { opt.Save(); } catch { }

        ModifyRequested?.Invoke(result, entry.Ctx);
    }
}
