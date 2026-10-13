using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClaudeProfiles;

/// <summary>Warm Claude-style palette, light and dark, following the Windows app theme.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var r = Application.Current.Resources;
        void Set(string key, string hex) => r[key] = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

        if (!dark)
        {
            Set("Bg", "#F5F4EE");
            Set("Surface", "#FFFFFF");
            Set("SurfaceAlt", "#F5F3EC");
            Set("Hover", "#EFEDE5");
            Set("Pressed", "#E6E3D9");
            Set("Line", "#E6E3DA");
            Set("LineStrong", "#D4D0C4");
            Set("Ink", "#1F1E1C");
            Set("Muted", "#6F6D66");
            Set("Faint", "#9C998F");
            Set("Accent", "#C96442");
            Set("AccentHover", "#B5583A");
            Set("AccentSoft", "#F6E6DE");
            Set("AccentInk", "#FFFFFF");
            Set("Good", "#3D8B5A");
            Set("Warn", "#A86A12");
            Set("WarnSoft", "#FBF0DC");
            Set("Danger", "#B4392C");
            Set("DangerHover", "#9E3125");
            Set("Scrim", "#661F1E1C");
            r["ShadowColor"] = (Color)ColorConverter.ConvertFromString("#1F1E1C");
            r["ShadowOpacity"] = 0.06;
        }
        else
        {
            Set("Bg", "#262624");
            Set("Surface", "#30302E");
            Set("SurfaceAlt", "#2B2B29");
            Set("Hover", "#3A3A37");
            Set("Pressed", "#444440");
            Set("Line", "#3E3E3A");
            Set("LineStrong", "#52514C");
            Set("Ink", "#F2F0E8");
            Set("Muted", "#A8A59C");
            Set("Faint", "#7D7A72");
            Set("Accent", "#D97757");
            Set("AccentHover", "#E3876A");
            Set("AccentSoft", "#4A3229");
            Set("AccentInk", "#FFFFFF");
            Set("Good", "#6CBF8A");
            Set("Warn", "#E0A84A");
            Set("WarnSoft", "#45391F");
            Set("Danger", "#E06A5C");
            Set("DangerHover", "#E98073");
            Set("Scrim", "#99000000");
            r["ShadowColor"] = Colors.Black;
            r["ShadowOpacity"] = 0.25;
        }
    }

    static Brush Freeze(Brush b) { b.Freeze(); return b; }
}

/// <summary>Small attached properties so one button template can serve every variant.</summary>
public static class Ui
{
    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached("HoverBackground", typeof(Brush), typeof(Ui), new PropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(9)));
    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(CornerRadiusProperty, v);

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject o) => (string)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string v) => o.SetValue(PlaceholderProperty, v);
}
