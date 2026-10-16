using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcClean;

/// <summary>Dark palette, fonts and drawing helpers shared by every custom control.</summary>
static class Theme
{
    // Ground → raised surfaces (blue-black graphite).
    public static readonly Color Bg = Color.FromArgb(0x11, 0x13, 0x17);
    public static readonly Color Surface = Color.FromArgb(0x18, 0x1B, 0x21);
    public static readonly Color Raised = Color.FromArgb(0x21, 0x25, 0x2D);
    public static readonly Color Hover = Color.FromArgb(0x26, 0x2B, 0x34);
    public static readonly Color Border = Color.FromArgb(0x2D, 0x33, 0x3D);

    // Text, all ≥ 4.5:1 on Bg and Surface.
    public static readonly Color Text = Color.FromArgb(0xE8, 0xEA, 0xEE);
    public static readonly Color Muted = Color.FromArgb(0x9B, 0xA4, 0xB1);
    public static readonly Color Faint = Color.FromArgb(0x7E, 0x87, 0x94);

    // Amber = "weight on disk"; red = "marked to go"; green = reclaimed.
    public static readonly Color Accent = Color.FromArgb(0xF5, 0xB0, 0x4C);
    public static readonly Color AccentHover = Color.FromArgb(0xFF, 0xC2, 0x6B);
    public static readonly Color OnAccent = Color.FromArgb(0x1A, 0x14, 0x0A);
    public static readonly Color Danger = Color.FromArgb(0xFF, 0x5F, 0x5F);
    public static readonly Color DangerHover = Color.FromArgb(0xFF, 0x7A, 0x7A);
    public static readonly Color DangerText = Color.FromArgb(0xFF, 0x8A, 0x8A);
    public static readonly Color Success = Color.FromArgb(0x5F, 0xD3, 0x9A);

    public static readonly Color MeterUsed = Color.FromArgb(0x4A, 0x52, 0x5F);

    static readonly string BodyFamily = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static readonly Font Body = new(BodyFamily, 9.75f);
    public static readonly Font Small = new(BodyFamily, 8.75f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font DialogTitle = new("Segoe UI Semibold", 13f);
    public static readonly Font Wordmark = new("Segoe UI", 12f, FontStyle.Bold);

    static bool Installed(string family) =>
        FontFamily.Families.Any(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase));

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = Round(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        using var path = Round(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    /// <summary>Folder glyph drawn in a square box, stroke scaled with the box.</summary>
    public static void DrawFolder(Graphics g, Color color, RectangleF box)
    {
        var s = box.Width / 16f;
        using var pen = new Pen(color, 1.4f * s) { LineJoin = LineJoin.Round };
        using var path = new GraphicsPath();
        float x = box.X, y = box.Y;
        path.AddLines(new[]
        {
            new PointF(x + 1.5f * s, y + 3.5f * s),
            new PointF(x + 6f * s, y + 3.5f * s),
            new PointF(x + 7.5f * s, y + 5f * s),
            new PointF(x + 14.5f * s, y + 5f * s),
            new PointF(x + 14.5f * s, y + 13f * s),
            new PointF(x + 1.5f * s, y + 13f * s),
        });
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    /// <summary>Document glyph with a folded corner.</summary>
    public static void DrawFile(Graphics g, Color color, RectangleF box)
    {
        var s = box.Width / 16f;
        using var pen = new Pen(color, 1.4f * s) { LineJoin = LineJoin.Round };
        float x = box.X, y = box.Y;
        using var path = new GraphicsPath();
        path.AddLines(new[]
        {
            new PointF(x + 3.5f * s, y + 1.5f * s),
            new PointF(x + 9.5f * s, y + 1.5f * s),
            new PointF(x + 12.5f * s, y + 4.5f * s),
            new PointF(x + 12.5f * s, y + 14.5f * s),
            new PointF(x + 3.5f * s, y + 14.5f * s),
        });
        path.CloseFigure();
        g.DrawPath(pen, path);
        g.DrawLines(pen, new[]
        {
            new PointF(x + 9.5f * s, y + 1.5f * s),
            new PointF(x + 9.5f * s, y + 4.5f * s),
            new PointF(x + 12.5f * s, y + 4.5f * s),
        });
    }

    /// <summary>Chevron pointing right (collapsed) or down (expanded).</summary>
    public static void DrawChevron(Graphics g, Color color, RectangleF box, bool expanded)
    {
        var s = box.Width / 16f;
        using var pen = new Pen(color, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
        var pts = expanded
            ? new[] { new PointF(cx - 4 * s, cy - 2 * s), new PointF(cx, cy + 2 * s), new PointF(cx + 4 * s, cy - 2 * s) }
            : new[] { new PointF(cx - 2 * s, cy - 4 * s), new PointF(cx + 2 * s, cy), new PointF(cx - 2 * s, cy + 4 * s) };
        g.DrawLines(pen, pts);
    }

    /// <summary>Rounded checkbox; checked state fills red because a tick means "delete".</summary>
    public static void DrawCheck(Graphics g, RectangleF box, bool isChecked, bool partial, bool hot)
    {
        var s = box.Width / 16f;
        var r = RectangleF.Inflate(box, -1.5f * s, -1.5f * s);
        if (isChecked)
        {
            FillRound(g, hot ? DangerHover : Danger, r, 3 * s);
            using var pen = new Pen(Bg, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(r.X + 3.2f * s, r.Y + 6.6f * s),
                new PointF(r.X + 5.6f * s, r.Y + 9f * s),
                new PointF(r.X + 9.8f * s, r.Y + 4f * s),
            });
            return;
        }

        DrawRound(g, hot ? Muted : Faint, r, 3 * s, 1.3f * s);
        if (partial)
            FillRound(g, Danger, RectangleF.Inflate(r, -3.5f * s, -3.5f * s), 1.5f * s);
    }

    // ---- Native window chrome --------------------------------------------------

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaCaptionColor = 35;

    /// <summary>Dark title bar tinted to the app ground (Windows 11; harmless no-op elsewhere).</summary>
    public static void ApplyDarkTitleBar(Form form)
    {
        var on = 1;
        DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
        var caption = ColorTranslator.ToWin32(Surface);
        DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref caption, sizeof(int));
    }
}
