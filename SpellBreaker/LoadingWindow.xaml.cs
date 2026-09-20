using System.Windows;

namespace SpellBreaker;

/// <summary>Modern loading splash shown while the app locates and inspects the target application.</summary>
public partial class LoadingWindow : Window
{
    public LoadingWindow()
    {
        InitializeComponent();
        Title = AppInfo.WindowTitle;
        VersionText.Text = $"v{AppInfo.Version}  |  Spell Breaker";
        StatusText.Text = App.Lang["LANG01"]; // WELCOME
    }

    /// <summary>Update progress (0-100) and status text; safe to call via Dispatcher.</summary>
    public void Report(int percent, string status)
    {
        Bar.Value = Math.Clamp(percent, 0, 100);
        StatusText.Text = status;
    }
}
