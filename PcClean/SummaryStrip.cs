using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcClean;

/// <summary>
/// Band under the toolbar: what was scanned (title + facts) on the left, and the
/// drive meter on the right. The meter shows used space with the currently ticked
/// bytes carved out in red, so a selection reads directly as space about to return.
/// </summary>
sealed class SummaryStrip : Control
{
    string _title = "Nothing scanned yet";
    string _detail = "Choose a folder or a whole drive above, then press Scan.";
    bool _isError;

    DriveInfo? _drive;
    long _driveFree;
    long _driveTotal;
    long _selected;

    public SummaryStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    public void SetHeadline(string title, string detail, bool isError = false)
    {
        _title = title;
        _detail = detail;
        _isError = isError;
        AccessibleName = $"{title}. {detail}";
        Invalidate();
    }

    /// <summary>Reads the drive's current free/total space; null hides the meter.</summary>
    public void SetDrive(DriveInfo? drive)
    {
        _drive = drive;
        try
        {
            _driveFree = drive?.TotalFreeSpace ?? 0;
            _driveTotal = drive?.TotalSize ?? 0;
        }
        catch (IOException)
        {
            _drive = null;
        }
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public long DriveFree => _drive is null ? -1 : _driveFree;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, 0, Height - 1, Width, Height - 1);

        int S(int v) => LogicalToDeviceUnits(v);
        var pad = S(20);
        var meterWidth = _drive is null ? 0 : Math.Min(S(300), Width / 2 - pad);
        var textRight = Width - pad - (meterWidth > 0 ? meterWidth + S(32) : 0);

        var titleH = TextRenderer.MeasureText(g, "Ag", Theme.Title).Height;
        var detailH = TextRenderer.MeasureText(g, "Ag", Theme.Body).Height;
        var top = (Height - titleH - detailH - S(2)) / 2;

        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, _title, Theme.Title, new Rectangle(pad, top, textRight - pad, titleH),
            _isError ? Theme.DangerText : Theme.Text, flags);
        TextRenderer.DrawText(g, _detail, Theme.Body, new Rectangle(pad, top + titleH + S(2), textRight - pad, detailH),
            _isError ? Theme.Text : Theme.Muted, flags);

        if (meterWidth > S(120))
            PaintMeter(g, new Rectangle(Width - pad - meterWidth, 0, meterWidth, Height));
    }

    void PaintMeter(Graphics g, Rectangle area)
    {
        int S(int v) => LogicalToDeviceUnits(v);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var labelH = TextRenderer.MeasureText(g, "Ag", Theme.Small).Height;
        var barH = S(8);
        var top = (area.Height - labelH - S(8) - barH - S(6) - labelH) / 2;

        const TextFormatFlags left = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        const TextFormatFlags right = left | TextFormatFlags.Right;

        var head = new Rectangle(area.X, top, area.Width, labelH);
        TextRenderer.DrawText(g, $"Drive {_drive!.Name.TrimEnd('\\')}", Theme.Small, head, Theme.Text, left);
        TextRenderer.DrawText(g, $"{Util.FormatSize(_driveFree)} free of {Util.FormatSize(_driveTotal)}",
            Theme.Small, head, Theme.Muted, right);

        var bar = new RectangleF(area.X, top + labelH + S(8), area.Width, barH);
        var radius = barH / 2f;
        Theme.FillRound(g, Theme.Raised, bar, radius);

        if (_driveTotal > 0)
        {
            var used = Math.Clamp((float)(_driveTotal - _driveFree) / _driveTotal, 0f, 1f);
            var sel = Math.Clamp((float)_selected / _driveTotal, 0f, used);
            var usedW = bar.Width * used;
            var selW = bar.Width * sel;

            using var clip = Theme.Round(bar, radius);
            g.SetClip(clip);
            using (var b = new SolidBrush(Theme.MeterUsed))
                g.FillRectangle(b, bar.X, bar.Y, usedW, bar.Height);
            if (selW > 0)
            {
                using var b = new SolidBrush(Theme.Danger);
                g.FillRectangle(b, bar.X + usedW - Math.Max(selW, S(3)), bar.Y, Math.Max(selW, S(3)), bar.Height);
            }
            g.ResetClip();
        }

        var foot = new Rectangle(area.X, (int)bar.Bottom + S(6), area.Width, labelH);
        if (_selected > 0)
        {
            TextRenderer.DrawText(g, $"{Util.FormatSize(_selected)} marked to delete", Theme.Small, foot, Theme.DangerText, left);
            TextRenderer.DrawText(g, $"→ {Util.FormatSize(_driveFree + _selected)} free", Theme.Small, foot, Theme.Muted, right);
        }
        else
        {
            var pct = _driveTotal > 0 ? (double)(_driveTotal - _driveFree) / _driveTotal : 0;
            TextRenderer.DrawText(g, $"{pct:P0} used", Theme.Small, foot, Theme.Muted, left);
        }
    }
}
