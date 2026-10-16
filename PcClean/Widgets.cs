using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcClean;

enum ButtonKind { Primary, Secondary, Danger }

/// <summary>Rounded, fully painted button with hover / pressed / focus / disabled states.</summary>
sealed class FlatButton : Button
{
    bool _hot;
    bool _down;

    public FlatButton(string text, ButtonKind kind)
    {
        Text = text;
        Kind = kind;
        Font = Theme.Strong;
        AutoSize = true;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public ButtonKind Kind { get; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
        return new Size(text.Width + LogicalToDeviceUnits(30), LogicalToDeviceUnits(34));
    }

    protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { _hot = _down = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var (fill, text) = Colors();
        var ring = LogicalToDeviceUnits(2);
        var r = new RectangleF(ring, ring, Width - ring * 2 - 1, Height - ring * 2 - 1);
        if (_down && Enabled)
            r.Offset(0, 1);

        var radius = LogicalToDeviceUnits(6);
        Theme.FillRound(g, fill, r, radius);
        if (Kind == ButtonKind.Secondary || !Enabled)
            Theme.DrawRound(g, Theme.Border, r, radius);

        if (Focused && ShowFocusCues)
            Theme.DrawRound(g, Theme.Accent, RectangleF.Inflate(r, ring - 0.5f, ring - 0.5f), radius + ring, 1.5f);

        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(r), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    (Color fill, Color text) Colors()
    {
        if (!Enabled)
            return (Theme.Surface, Theme.Faint);

        return Kind switch
        {
            ButtonKind.Primary => (_hot ? Theme.AccentHover : Theme.Accent, Theme.OnAccent),
            ButtonKind.Danger => (_hot ? Theme.DangerHover : Theme.Danger, Theme.Bg),
            _ => (_hot ? Theme.Hover : Theme.Raised, Theme.Text),
        };
    }
}

/// <summary>Borderless TextBox inside a painted rounded field; the border turns amber on focus.</summary>
sealed class PathField : Panel
{
    public PathField()
    {
        BackColor = Theme.Raised;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Raised,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            Dock = DockStyle.Fill,
            PlaceholderText = @"Folder or drive, e.g. C:\Users or D:",
        };
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.EnabledChanged += (_, _) => Invalidate();
        Controls.Add(Box);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.IBeam;
    }

    public TextBox Box { get; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Centre the single-line box vertically inside the taller field.
        var padX = LogicalToDeviceUnits(12);
        var padY = Math.Max(0, (Height - Box.PreferredHeight) / 2);
        Padding = new Padding(padX, padY, padX, 0);
        base.OnLayout(levent);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Box.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = LogicalToDeviceUnits(6);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Theme.Raised, r, radius);
        var focused = Box.Focused;
        Theme.DrawRound(g, focused ? Theme.Accent : Theme.Border, r, radius, focused ? 1.5f : 1f);
    }
}

/// <summary>
/// Thin progress rail. Determinate mode fills to <see cref="Value"/> (0..1);
/// indeterminate mode runs a single amber sweep with an ease-in-out curve.
/// </summary>
sealed class ProgressLine : Control
{
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    readonly DateTime _epoch = DateTime.UtcNow;
    bool _running;
    bool _indeterminate;
    float _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        _timer.Tick += (_, _) => Invalidate();
    }

    public void StartIndeterminate() { _indeterminate = true; _running = true; _timer.Start(); Invalidate(); }
    public void StartDeterminate() { _indeterminate = false; _running = true; _value = 0; _timer.Stop(); Invalidate(); }
    public void Stop() { _running = false; _timer.Stop(); Invalidate(); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Border);
        if (!_running)
            return;

        using var brush = new SolidBrush(Theme.Accent);
        if (!_indeterminate)
        {
            g.FillRectangle(brush, 0, 0, Width * _value, Height);
            return;
        }

        // 1.6 s cycle; segment is 30% of the width and eases across.
        var t = (float)((DateTime.UtcNow - _epoch).TotalSeconds % 1.6 / 1.6);
        var eased = t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
        var seg = Width * 0.3f;
        var x = -seg + (Width + seg) * eased;
        g.FillRectangle(brush, x, 0, seg, Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
