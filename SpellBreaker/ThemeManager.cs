using System.Windows;
using System.Windows.Media;

namespace SpellBreaker;

public static class ThemeManager
{
    /// <summary>Apply LIGHT or DARK colors to the shared app brushes (mirrors the HTA themes).</summary>
    public static void Apply(string theme)
    {
        var r = System.Windows.Application.Current.Resources;
        void Set(string key, string color) =>
            r[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

        if (theme == "DARK")
        {
            Set("WindowBg", "#191919");
            Set("BodyBg", "#282828");
            Set("Fg", "White");
            Set("InputBg", "#353535");
            Set("InputFg", "White");
            Set("PopupBg", "#2F2F2F");
            Set("Legend", "LightBlue");
            Set("ButtonBg", "#505050");
            Set("ButtonHover", "#666666");
            Set("ButtonPressed", "#3C3C3C");
            Set("GroupBorder", "DarkGray");
            Set("OkFg", "#5FD35F");
            Set("FailFg", "#FF6B6B");
            Set("DimFg", "#AAAAAA");
            Set("Accent", "#8B6CF0");
            Set("HighlightBg", "#5B4BB8");
        }
        else
        {
            Set("WindowBg", "#EBEBEB");
            Set("BodyBg", "White");
            Set("Fg", "Black");
            Set("InputBg", "White");
            Set("InputFg", "Black");
            Set("PopupBg", "White");
            Set("Legend", "Blue");
            Set("ButtonBg", "#CFCFCF");
            Set("ButtonHover", "#B8B8B8");
            Set("ButtonPressed", "#9E9E9E");
            Set("GroupBorder", "DarkGray");
            Set("OkFg", "Green");
            Set("FailFg", "Red");
            Set("DimFg", "Gray");
            Set("Accent", "#6B4FD8");
            Set("HighlightBg", "#6B4FD8");
        }
    }
}
