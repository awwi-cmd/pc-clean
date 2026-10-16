using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcClean;

/// <summary>
/// Owner-drawn, virtualised folder tree. Only expanded branches are flattened into
/// rows, so huge scans stay cheap. Each row carries a share-of-parent bar — the
/// app's signature: you see at a glance which child is eating its folder.
/// Tick state lives in the <see cref="FileNode"/> model; this control only reads it
/// and raises <see cref="CheckToggled"/>.
/// </summary>
sealed class SizeTree : Control
{
    readonly HashSet<FileNode> _expanded = new();
    readonly List<Row> _rows = new();
    readonly ToolTip _tip = new() { InitialDelay = 600, ReshowDelay = 200 };

    FileNode? _root;
    HashSet<FileNode> _partial = new();
    int _scroll;
    int _focus = -1;
    int _hover = -1;
    bool _hoverCheck;
    int _thumbDragOffset = -1;
    string _tipText = "";

    readonly record struct Row(FileNode Node, int Depth);

    public SizeTree()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.Outline;
        AccessibleName = "Folder tree";
    }

    public event Action<FileNode>? CheckToggled;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyTitle { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyHint { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FileNode? Root
    {
        get => _root;
        set
        {
            _root = value;
            _expanded.Clear();
            _partial = new();
            _scroll = 0;
            if (value is not null)
                _expanded.Add(value);
            Rebuild();
            _focus = _rows.Count > 0 ? 0 : -1;
            Invalidate();
        }
    }

    /// <summary>Folders that are not ticked themselves but contain ticked items.</summary>
    public void SetPartial(HashSet<FileNode> partial)
    {
        _partial = partial;
        Invalidate();
    }

    /// <summary>Re-flattens after the model changed (e.g. nodes deleted).</summary>
    public void Reload()
    {
        var focused = _focus >= 0 && _focus < _rows.Count ? _rows[_focus].Node : null;
        Rebuild();
        _focus = focused is null ? -1 : _rows.FindIndex(r => r.Node == focused);
        if (_focus < 0 && _rows.Count > 0)
            _focus = 0;
        ClampScroll();
        Invalidate();
    }

    // ---- Layout -----------------------------------------------------------------

    int S(int v) => LogicalToDeviceUnits(v);
    int RowH => S(32);
    int HeaderH => S(34);
    int BarW => S(8);
    int ContentH => _rows.Count * RowH;
    int ViewH => Math.Max(0, Height - HeaderH);
    int MaxScroll => Math.Max(0, ContentH - ViewH);

    // Right-hand columns: share bar, size, files. Bar hides on narrow windows.
    (int nameRight, int barX, int barW, int sizeX, int sizeW, int filesX, int filesW) Columns()
    {
        int right = Width - S(16) - BarW;
        int filesW = S(96), sizeW = S(92), gap = S(20);
        int filesX = right - filesW;
        int sizeX = filesX - gap - sizeW;
        int barW = Width >= S(720) ? S(150) : 0;
        int barX = sizeX - gap - barW;
        int nameRight = (barW > 0 ? barX : sizeX) - gap;
        return (nameRight, barX, barW, sizeX, sizeW, filesX, filesW);
    }

    int Indent(int depth) => S(14) + depth * S(20);
    Rectangle ChevronRect(int y, int depth) => new(Indent(depth), y + (RowH - S(16)) / 2, S(16), S(16));
    Rectangle CheckRect(int y, int depth) => new(Indent(depth) + S(22), y + (RowH - S(18)) / 2, S(18), S(18));

    void Rebuild()
    {
        _rows.Clear();
        if (_root is not null)
            Add(_root, 0);

        void Add(FileNode node, int depth)
        {
            _rows.Add(new Row(node, depth));
            if (node.IsDirectory && _expanded.Contains(node))
                foreach (var child in node.Children)
                    Add(child, depth + 1);
        }
    }

    void ClampScroll() => _scroll = Math.Clamp(_scroll, 0, MaxScroll);

    void Toggle(int index, bool? expand = null)
    {
        var node = _rows[index].Node;
        if (!node.IsDirectory || node.Children.Count == 0)
            return;

        var open = expand ?? !_expanded.Contains(node);
        if (open == _expanded.Contains(node))
            return;
        if (open)
            _expanded.Add(node);
        else
            _expanded.Remove(node);

        Rebuild();
        _focus = _rows.FindIndex(r => r.Node == node);
        ClampScroll();
        Invalidate();
    }

    void SetFocus(int index)
    {
        if (_rows.Count == 0)
            return;
        _focus = Math.Clamp(index, 0, _rows.Count - 1);
        var top = _focus * RowH;
        if (top < _scroll)
            _scroll = top;
        else if (top + RowH > _scroll + ViewH)
            _scroll = top + RowH - ViewH;
        ClampScroll();
        AccessibilityNotifyClients(AccessibleEvents.Focus, -1);
        AccessibleDescription = RowSpeech(_rows[_focus].Node);
        Invalidate();
    }

    static string RowSpeech(FileNode n) =>
        $"{n.Name}, {Util.FormatSize(n.Size)}" +
        (n.IsDirectory ? $", {Util.Count(n.FileCount, "file")}" : "") +
        (n.Checked ? ", marked for delete" : "");

    int RowAt(int y)
    {
        if (y < HeaderH)
            return -1;
        var i = (y - HeaderH + _scroll) / RowH;
        return i < _rows.Count ? i : -1;
    }

    // ---- Input ------------------------------------------------------------------

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or
            Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_rows.Count == 0)
            return;

        var page = Math.Max(1, ViewH / RowH - 1);
        var current = Math.Max(0, _focus);
        switch (e.KeyCode)
        {
            case Keys.Up: SetFocus(current - 1); break;
            case Keys.Down: SetFocus(current + 1); break;
            case Keys.PageUp: SetFocus(current - page); break;
            case Keys.PageDown: SetFocus(current + page); break;
            case Keys.Home: SetFocus(0); break;
            case Keys.End: SetFocus(_rows.Count - 1); break;
            case Keys.Right:
                var r = _rows[current];
                if (r.Node.IsDirectory && r.Node.Children.Count > 0 && !_expanded.Contains(r.Node))
                    Toggle(current, true);
                else if (current + 1 < _rows.Count && _rows[current + 1].Depth > r.Depth)
                    SetFocus(current + 1);
                break;
            case Keys.Left:
                var l = _rows[current];
                if (_expanded.Contains(l.Node) && l.Node.Children.Count > 0)
                    Toggle(current, false);
                else if (l.Node.Parent is { } parent)
                    SetFocus(_rows.FindIndex(x => x.Node == parent));
                break;
            case Keys.Space:
                CheckToggled?.Invoke(_rows[current].Node);
                Invalidate();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left)
            return;

        // Scrollbar: drag thumb, or page on track click.
        if (MaxScroll > 0 && e.X >= Width - BarW - S(4))
        {
            var thumb = ThumbRect();
            if (thumb.Contains(e.Location))
                _thumbDragOffset = e.Y - thumb.Y;
            else
                _scroll += (e.Y < thumb.Y ? -1 : 1) * ViewH;
            ClampScroll();
            Invalidate();
            return;
        }

        var i = RowAt(e.Y);
        if (i < 0)
            return;

        var y = HeaderH + i * RowH - _scroll;
        var row = _rows[i];
        if (row.Node.IsDirectory && ChevronRect(y, row.Depth).Contains(e.Location))
        {
            Toggle(i);
            return;
        }

        SetFocus(i);
        var hit = CheckRect(y, row.Depth);
        hit.Inflate(S(3), S(3));
        if (hit.Contains(e.Location))
        {
            CheckToggled?.Invoke(row.Node);
            Invalidate();
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        var i = RowAt(e.Y);
        if (i < 0 || e.X >= Width - BarW - S(4))
            return;
        var y = HeaderH + i * RowH - _scroll;
        var row = _rows[i];
        if (!ChevronRect(y, row.Depth).Contains(e.Location) && !CheckRect(y, row.Depth).Contains(e.Location))
            Toggle(i);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _thumbDragOffset = -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_thumbDragOffset >= 0)
        {
            var track = ViewH - ThumbRect().Height;
            var pos = e.Y - HeaderH - _thumbDragOffset;
            _scroll = track > 0 ? (int)((long)pos * MaxScroll / track) : 0;
            ClampScroll();
            Invalidate();
            return;
        }

        var i = RowAt(e.Y);
        var onCheck = false;
        if (i >= 0)
        {
            var y = HeaderH + i * RowH - _scroll;
            onCheck = CheckRect(y, _rows[i].Depth).Contains(e.Location);
        }

        if (i != _hover || onCheck != _hoverCheck)
        {
            _hover = i;
            _hoverCheck = onCheck;
            Cursor = onCheck ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        var tip = i >= 0 ? _rows[i].Node.FullPath : "";
        if (tip != _tipText)
        {
            _tipText = tip;
            _tip.SetToolTip(this, tip);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverCheck = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll -= e.Delta * 3 * RowH / 120;
        ClampScroll();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ClampScroll();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    // ---- Paint ------------------------------------------------------------------

    Rectangle ThumbRect()
    {
        var h = Math.Max(S(32), (int)((long)ViewH * ViewH / Math.Max(1, ContentH)));
        var track = ViewH - h;
        var y = HeaderH + (MaxScroll > 0 ? (int)((long)_scroll * track / MaxScroll) : 0);
        return new Rectangle(Width - BarW - S(4), y, BarW, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cols = Columns();

        PaintHeader(g, cols);

        if (_rows.Count == 0)
        {
            PaintEmpty(g);
            return;
        }

        g.SetClip(new Rectangle(0, HeaderH, Width, ViewH));
        var first = _scroll / RowH;
        var last = Math.Min(_rows.Count - 1, (_scroll + ViewH) / RowH);
        for (var i = first; i <= last; i++)
            PaintRow(g, i, HeaderH + i * RowH - _scroll, cols);
        g.ResetClip();

        if (MaxScroll > 0)
        {
            var thumb = ThumbRect();
            Theme.FillRound(g, _thumbDragOffset >= 0 ? Theme.Muted : Theme.Border, thumb, BarW / 2f);
        }
    }

    void PaintHeader(Graphics g, (int nameRight, int barX, int barW, int sizeX, int sizeW, int filesX, int filesW) c)
    {
        using (var b = new SolidBrush(Theme.Bg))
            g.FillRectangle(b, 0, 0, Width, HeaderH);
        using (var p = new Pen(Theme.Border))
            g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);

        const TextFormatFlags f = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var h = HeaderH;
        TextRenderer.DrawText(g, "Name", Theme.Small, new Rectangle(Indent(0) + S(22), 0, c.nameRight, h), Theme.Faint, f);
        if (c.barW > 0)
            TextRenderer.DrawText(g, "Share of folder", Theme.Small, new Rectangle(c.barX, 0, c.barW, h), Theme.Faint, f);
        TextRenderer.DrawText(g, "Size", Theme.Small, new Rectangle(c.sizeX, 0, c.sizeW, h), Theme.Faint, f | TextFormatFlags.Right);
        TextRenderer.DrawText(g, "Files", Theme.Small, new Rectangle(c.filesX, 0, c.filesW, h), Theme.Faint, f | TextFormatFlags.Right);
    }

    void PaintEmpty(Graphics g)
    {
        var w = Math.Min(Width - S(48), S(520));
        var area = new Rectangle((Width - w) / 2, HeaderH, w, ViewH);
        var titleH = TextRenderer.MeasureText(g, "Ag", Theme.DialogTitle).Height;
        var top = area.Y + area.Height / 2 - titleH;
        const TextFormatFlags f = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
        TextRenderer.DrawText(g, EmptyTitle, Theme.DialogTitle, new Rectangle(area.X, top, area.Width, titleH),
            Theme.Text, f | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, EmptyHint, Theme.Body, new Rectangle(area.X, top + titleH + S(6), area.Width, S(80)),
            Theme.Muted, f);
    }

    void PaintRow(Graphics g, int i, int y, (int nameRight, int barX, int barW, int sizeX, int sizeW, int filesX, int filesW) c)
    {
        var row = _rows[i];
        var node = row.Node;
        var dim = !Enabled;
        var rowRect = new Rectangle(0, y, Width - BarW - S(8), RowH);

        // Background: ticked rows get a faint red wash; focus shows a rounded amber-edged band.
        if (node.Checked)
            using (var b = new SolidBrush(Theme.Mix(Theme.Bg, Theme.Danger, 0.09f)))
                g.FillRectangle(b, rowRect);
        if (i == _hover && !dim)
            using (var b = new SolidBrush(Color.FromArgb(18, Theme.Text)))
                g.FillRectangle(b, rowRect);
        if (i == _focus)
        {
            var fr = new RectangleF(S(4), y + 1, rowRect.Width - S(4), RowH - 2);
            if (Focused)
                Theme.DrawRound(g, Color.FromArgb(150, Theme.Accent), fr, S(5), 1.2f);
            else
                Theme.FillRound(g, Color.FromArgb(14, Theme.Text), fr, S(5));
        }

        var text = dim ? Theme.Faint : Theme.Text;
        var muted = dim ? Color.FromArgb(0x5A, 0x61, 0x6C) : Theme.Muted;

        if (node.IsDirectory && node.Children.Count > 0)
            Theme.DrawChevron(g, muted, ChevronRect(y, row.Depth), _expanded.Contains(node));

        var check = CheckRect(y, row.Depth);
        Theme.DrawCheck(g, check, node.Checked, _partial.Contains(node), i == _hover && _hoverCheck);

        var icon = new RectangleF(check.Right + S(10), y + (RowH - S(16)) / 2f, S(16), S(16));
        var iconColor = node.Checked ? Theme.DangerText : node.IsDirectory ? Theme.Accent : muted;
        if (node.IsDirectory)
            Theme.DrawFolder(g, iconColor, icon);
        else
            Theme.DrawFile(g, iconColor, icon);

        const TextFormatFlags f = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var nameX = (int)icon.Right + S(10);
        var nameFont = row.Depth == 0 ? Theme.Strong : Theme.Body;
        TextRenderer.DrawText(g, node.Name, nameFont, new Rectangle(nameX, y, Math.Max(0, c.nameRight - nameX), RowH),
            node.Checked && !dim ? Theme.DangerText : text, f | TextFormatFlags.EndEllipsis);

        if (c.barW > 0)
            PaintShare(g, node, new Rectangle(c.barX, y, c.barW, RowH), dim);

        TextRenderer.DrawText(g, Util.FormatSize(node.Size), Theme.Body, new Rectangle(c.sizeX, y, c.sizeW, RowH),
            text, f | TextFormatFlags.Right);
        var files = node.IsDirectory ? node.FileCount.ToString("N0") : "—";
        TextRenderer.DrawText(g, files, Theme.Body, new Rectangle(c.filesX, y, c.filesW, RowH), muted, f | TextFormatFlags.Right);
    }

    void PaintShare(Graphics g, FileNode node, Rectangle cell, bool dim)
    {
        var parentSize = node.Parent?.Size ?? node.Size;
        var share = parentSize > 0 ? (float)node.Size / parentSize : 0f;

        var pctW = S(40);
        var barH = S(6);
        var track = new RectangleF(cell.X, cell.Y + (cell.Height - barH) / 2f, cell.Width - pctW - S(8), barH);
        Theme.FillRound(g, Theme.Raised, track, barH / 2f);

        if (share > 0)
        {
            // Heavier shares glow brighter: the eye lands on the biggest offenders first.
            var color = node.Checked ? Theme.Danger : Theme.Mix(Theme.MeterUsed, Theme.Accent, 0.35f + 0.65f * MathF.Sqrt(share));
            if (dim)
                color = Theme.Mix(Theme.Bg, color, 0.4f);
            var w = Math.Max(barH, track.Width * share);
            Theme.FillRound(g, color, new RectangleF(track.X, track.Y, w, barH), barH / 2f);
        }

        var pct = share >= 0.995f ? "100%" : share < 0.01f && share > 0 ? "<1%" : $"{share:P0}";
        TextRenderer.DrawText(g, pct, Theme.Small, new Rectangle((int)track.Right, cell.Y, pctW + S(8), cell.Height),
            dim ? Theme.Faint : Theme.Muted,
            TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _tip.Dispose();
        base.Dispose(disposing);
    }
}
