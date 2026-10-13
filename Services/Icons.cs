using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClaudeProfiles.Services;

/// <summary>Draws the app mark and per-profile avatars, and writes them as .ico files.</summary>
public static class Icons
{
    public static readonly Color Clay = Color.FromRgb(0xD9, 0x77, 0x57);

    public static string Initial(string name)
    {
        var t = name.Trim();
        if (t.Length == 0) return "?";
        var first = char.ConvertFromUtf32(char.ConvertToUtf32(t, 0));
        return first.ToUpper(CultureInfo.CurrentCulture);
    }

    public static Color ParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return Clay; }
    }

    public static BitmapSource Avatar(string colorHex, string name, int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double r = size / 2.0;
            dc.DrawEllipse(new SolidColorBrush(ParseColor(colorHex)), null, new Point(r, r), r, r);
            var text = new FormattedText(Initial(name), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Georgia"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                size * 0.52, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2 - size * 0.02));
        }
        return Render(dv, size);
    }

    /// <summary>Three stacked windows on a clay tile.</summary>
    public static BitmapSource AppMark(int size)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double s = size;
            dc.DrawRoundedRectangle(new SolidColorBrush(Clay), null, new Rect(0, 0, s, s), s * 0.22, s * 0.22);
            var white = Colors.White;
            void Card(double x, double y, double opacity) =>
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), white.R, white.G, white.B)),
                    null, new Rect(s * x, s * y, s * 0.50, s * 0.36), s * 0.07, s * 0.07);
            Card(0.32, 0.17, 0.45);
            Card(0.25, 0.30, 0.70);
            Card(0.18, 0.43, 1.0);
            dc.DrawEllipse(new SolidColorBrush(Clay), null, new Point(s * 0.27, s * 0.52), s * 0.035, s * 0.035);
        }
        return Render(dv, size);
    }

    static BitmapSource Render(Visual v, int size)
    {
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed entries).</summary>
    public static void WriteIco(string path, Func<int, BitmapSource> draw)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 256 };
        var images = sizes.Select(sz =>
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(draw(sz)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (sz, data: ms.ToArray());
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (sz, data) in images)
        {
            w.Write((byte)(sz >= 256 ? 0 : sz)); w.Write((byte)(sz >= 256 ? 0 : sz));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(data.Length); w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
    }

    public static string ProfileIcon(ProfileRecord p)
    {
        var path = Path.Combine(AppPaths.IconsDir, $"{p.Id}-{p.Color.TrimStart('#')}-{Initial(p.Name)}.ico");
        if (!File.Exists(path)) WriteIco(path, sz => Avatar(p.Color, p.Name, sz));
        return path;
    }

    public static string AppIcon()
    {
        var path = Path.Combine(AppPaths.IconsDir, "app.ico");
        if (!File.Exists(path)) WriteIco(path, AppMark);
        return path;
    }
}
