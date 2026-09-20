using System.Windows;
using System.Windows.Controls;

namespace SpellBreaker;

public partial class OptionsView : UserControl
{
    public event Action? BackRequested;
    public event Action? Saved;

    private readonly Options _opt;
    private bool _loading; // suppress combo change handlers while populating

    public OptionsView()
    {
        InitializeComponent();
        _opt = App.Opts;
        _loading = true;
        ApplyLanguage(App.Lang);
        PopulateCombos();
        LoadValues();
        _loading = false;
    }

    private void ApplyLanguage(Localization L)
    {
        LangHeader.Text = L["LANGOPT00"];
        LangDesc.Text = L["LANGOPT01"];
        ForceLangLabel.Text = L["LANGOPT02"];
        SoundsHeader.Text = L["LANGOPT06"];
        SoundsDesc.Text = L["LANGOPT07"];
        SoundsCheck.Content = L["LANGOPT08"];
        SuccessLabel.Text = L["LANGOPT09"];
        FailureLabel.Text = L["LANGOPT10"];
        ModHeader.Text = L["LANGOPT23"];
        PromosCheck.Content = L["LANGOPT20"];
        OverlayCheck.Content = L["LANGOPT21"];
        OthersHeader.Text = L["LANGOPT12"];
        ShortcutCheck.Content = L["LANGOPT14"];
        SaveButton.Content = L["LANGOPT17"];
        CancelButton.Content = L["LANG59"];
        ThemeLight.Content = L["LANGOPT18"];
        ThemeDark.Content = L["LANGOPT19"];
        FillSoundCombos();
        UpdateStateLabels();
    }

    private void PopulateCombos()
    {
        LangCombo.Items.Clear();
        LangCombo.Items.Add(MakeItem("AUTO", "AUTO"));
        foreach (var code in Localization.AvailableLanguages())
            LangCombo.Items.Add(MakeItem(code, code));
        FillSoundCombos();
    }

    private void FillSoundCombos()
    {
        // keep the current (possibly unsaved) selection when re-filled by a language preview
        FillSoundCombo(SuccessCombo, ComboTag(SuccessCombo) is { Length: > 0 } s ? s : _opt.SoundSuccess);
        FillSoundCombo(FailureCombo, ComboTag(FailureCombo) is { Length: > 0 } f ? f : _opt.SoundFailure);
    }

    private void FillSoundCombo(ComboBox cb, string current)
    {
        var L = App.Lang;
        cb.Items.Clear();
        cb.Items.Add(MakeItem(L["LANGOPT24"], "1"));
        cb.Items.Add(MakeItem(L["LANGOPT25"], "2"));
        cb.Items.Add(MakeItem(L["LANGOPT26"], "3"));
        cb.Items.Add(MakeItem(L["LANGOPT11"], "USER"));
        SelectCombo(cb, current);
    }

    private ComboBoxItem MakeItem(string content, string tag) =>
        new() { Content = content, Tag = tag };

    private void LoadValues()
    {
        var forced = _opt.ForceLanguage;
        LangFile.Text = string.IsNullOrEmpty(forced) ? "( AUTO )" : $"( {forced} )";
        SelectCombo(LangCombo, string.IsNullOrEmpty(forced) ? "AUTO" : forced);
        SelectCombo(ThemeCombo, string.IsNullOrEmpty(_opt.ForceThemeColor) ? "AUTO" : _opt.ForceThemeColor);
        SoundsCheck.IsChecked = _opt.EnableSounds;
        SelectCombo(SuccessCombo, _opt.SoundSuccess);
        SelectCombo(FailureCombo, _opt.SoundFailure);
        PromosCheck.IsChecked = _opt.ModPromos;
        OverlayCheck.IsChecked = _opt.ModOverlay;
        UpdateStateLabels();
        SoundsToggled(null, null);
    }

    private static void SelectCombo(ComboBox cb, string tag)
    {
        foreach (ComboBoxItem i in cb.Items)
            if ((i.Tag as string) == tag) { cb.SelectedItem = i; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private static string ComboTag(ComboBox cb) =>
        (cb.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void UpdateStateLabels()
    {
        SoundsState.Text = SoundsCheck.IsChecked == true ? App.Lang["LANGOPT15"] : App.Lang["LANGOPT16"];
    }

    private void SoundsToggled(object? sender, RoutedEventArgs? e)
    {
        if (SoundsPanel == null) return;
        SoundsPanel.IsEnabled = SoundsCheck.IsChecked == true;
        UpdateStateLabels();
    }

    /// <summary>Speaker icon click: preview the combo's current selection (ignores the master toggle).</summary>
    private void PlaySoundPreview(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var success = (btn.Tag as string) == "Success";
        var cb = success ? SuccessCombo : FailureCombo;
        SoundPlayer.Preview(success ? SoundKind.Success : SoundKind.Failure,
            ComboTag(cb), success ? _opt.SoundSuccessUser : _opt.SoundFailureUser);
    }

    /// <summary>User selected "User" in a sound combo: reuse saved file or pick one; revert on cancel.</summary>
    private void SoundComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || sender is not ComboBox cb || ComboTag(cb) != "USER") return;
        var isSuccess = ReferenceEquals(cb, SuccessCombo);
        var saved = isSuccess ? _opt.SoundSuccessUser : _opt.SoundFailureUser;
        var keep = !string.IsNullOrWhiteSpace(saved) && System.IO.File.Exists(saved) &&
            MessageBox.Show(App.Lang["LANGOPT27"], $"{AppInfo.Title} Options",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        var picked = "";
        if (!keep)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = App.Lang["LANGOPT28"],
                Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.ogg|All files|*.*",
            };
            if (dlg.ShowDialog() == true) picked = dlg.FileName;
        }

        _loading = true;
        if (keep || !string.IsNullOrEmpty(picked))
        {
            if (!string.IsNullOrEmpty(picked))
            {
                if (isSuccess) _opt.SoundSuccessUser = picked; else _opt.SoundFailureUser = picked;
            }
            SelectCombo(cb, "USER"); // stays on User
        }
        else
        {
            // cancelled: revert to the saved selection
            SelectCombo(cb, isSuccess ? _opt.SoundSuccess : _opt.SoundFailure);
        }
        _loading = false;
    }

    private void LangComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var tag = ComboTag(LangCombo);
        var code = tag == "AUTO" ? "" : tag;
        // live-preview the selected UI language like the HTA does
        _loading = true;
        ApplyLanguage(Localization.Load(code));
        _loading = false;
        LangFile.Text = tag == "AUTO" ? "( AUTO )" : $"( {tag} )";
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var tag = ComboTag(ThemeCombo);
        if (string.IsNullOrEmpty(tag)) return;
        var theme = tag == "AUTO" ? new Options { ForceThemeColor = "" }.EffectiveTheme() : tag;
        ThemeManager.Apply(theme);
    }

    private void CancelClicked(object sender, RoutedEventArgs e)
    {
        App.ReloadSettings();
        BackRequested?.Invoke();
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        var langTag = ComboTag(LangCombo);
        _opt.ForceLanguage = langTag == "AUTO" ? "" : langTag;
        var themeTag = ComboTag(ThemeCombo);
        _opt.ForceThemeColor = themeTag == "AUTO" ? "" : themeTag;
        _opt.EnableSounds = SoundsCheck.IsChecked == true;
        _opt.SoundSuccess = ComboTag(SuccessCombo) is { Length: > 0 } s ? s : "1";
        _opt.SoundFailure = ComboTag(FailureCombo) is { Length: > 0 } f ? f : "1";
        _opt.ModPromos = PromosCheck.IsChecked == true;
        _opt.ModOverlay = OverlayCheck.IsChecked == true;
        _opt.Save();

        if (ShortcutCheck.IsChecked == true)
        {
            try { CliCommands.CreateDesktopShortcut(); } catch { }
        }

        App.ReloadSettings();
        Saved?.Invoke();
        BackRequested?.Invoke();
    }
}
