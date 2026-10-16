using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PcClean;

enum ItemGlyph { Folder, File, Error }

sealed record DialogItem(ItemGlyph Glyph, string Path, string Detail, string Trailing = "");

/// <summary>
/// Dark modal dialog matching the main window. Title, body, an optional itemised
/// list (paths elided in the middle so the file name survives), an optional red
/// footnote, and a right-aligned button row.
/// </summary>
sealed class ThemedDialog : Form
{
    const int MaxVisibleItems = 8;

    ThemedDialog(string title, string body, IReadOnlyList<DialogItem> items, string? overflow,
        string? warning, string confirmText, ButtonKind confirmKind, string? cancelText)
    {
        Text = "PC Clean";
        Font = Theme.Body;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(28, 24, 28, 20);

        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Location = new Point(28, 24),
        };
        const int width = 560;

        stack.Controls.Add(new Label
        {
            Text = title,
            Font = Theme.DialogTitle,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Margin = new Padding(0, 0, 0, 8),
            UseMnemonic = false,
        });
        stack.Controls.Add(new Label
        {
            Text = body,
            ForeColor = Theme.Muted,
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Margin = new Padding(0, 0, 0, items.Count > 0 ? 16 : 4),
            UseMnemonic = false,
        });

        if (items.Count > 0)
        {
            stack.Controls.Add(new ItemList(items.Take(MaxVisibleItems).ToList())
            {
                Width = width,
                Margin = new Padding(0, 0, 0, overflow is null ? 12 : 6),
            });
            if (overflow is not null)
                stack.Controls.Add(new Label
                {
                    Text = overflow,
                    ForeColor = Theme.Faint,
                    AutoSize = true,
                    Margin = new Padding(12, 0, 0, 12),
                    UseMnemonic = false,
                });
        }

        if (warning is not null)
            stack.Controls.Add(new Label
            {
                Text = warning,
                ForeColor = Theme.DangerText,
                Font = Theme.Strong,
                AutoSize = true,
                MaximumSize = new Size(width, 0),
                Margin = new Padding(0, 4, 0, 4),
                UseMnemonic = false,
            });

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 16, 0, 0),
            MinimumSize = new Size(width, 0),
            BackColor = Theme.Surface,
        };
        stack.Controls.Add(buttons);
        Controls.Add(stack);

        var confirm = new FlatButton(confirmText, confirmKind) { DialogResult = DialogResult.OK, Margin = new Padding(8, 0, 0, 0) };
        buttons.Controls.Add(confirm);
        if (cancelText is not null)
        {
            var cancel = new FlatButton(cancelText, ButtonKind.Secondary) { DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
            buttons.Controls.Add(cancel);
            CancelButton = cancel;
            // Destructive confirms start on Cancel: Enter must never delete by accident.
            ActiveControl = cancel;
        }
        else
        {
            AcceptButton = confirm;
            CancelButton = confirm;
            ActiveControl = confirm;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }

    /// <summary>Asks before a destructive action. True only when the user confirms.</summary>
    public static bool Confirm(IWin32Window owner, string title, string body, IReadOnlyList<DialogItem> items,
        string? overflow, string warning, string confirmText)
    {
        using var dlg = new ThemedDialog(title, body, items, overflow, warning, confirmText, ButtonKind.Danger, "Cancel");
        dlg.StartPosition = FormStartPosition.CenterParent;
        return dlg.ShowDialog(owner) == DialogResult.OK;
    }

    /// <summary>One-button notice. <paramref name="owner"/> may be null before the main window exists.</summary>
    public static void Inform(IWin32Window? owner, string title, string body,
        IReadOnlyList<DialogItem>? items = null, string? overflow = null)
    {
        using var dlg = new ThemedDialog(title, body, items ?? [], overflow, null, "OK", ButtonKind.Secondary, null);
        dlg.StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        dlg.ShowDialog(owner);
    }

    /// <summary>Static, painted list of items; tooltips carry the full path.</summary>
    sealed class ItemList : Control
    {
        readonly IReadOnlyList<DialogItem> _items;
        readonly ToolTip _tip = new();
        int _tipIndex = -1;

        public ItemList(IReadOnlyList<DialogItem> items)
        {
            _items = items;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            AccessibleRole = AccessibleRole.List;
            AccessibleName = string.Join("; ", items.Select(i => $"{i.Path} {i.Detail} {i.Trailing}"));
            Height = 48 * items.Count + 2;
        }

        // Height is set in logical pixels and scaled by the dialog's DPI autoscale.
        int RowH => (Height - 2) / _items.Count;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var i = Math.Min(_items.Count - 1, e.Y / RowH);
            if (i != _tipIndex)
            {
                _tipIndex = i;
                _tip.SetToolTip(this, _items[i].Path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int S(int v) => LogicalToDeviceUnits(v);
            var g = e.Graphics;
            g.Clear(Theme.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var frame = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Bg, frame, S(8));
            Theme.DrawRound(g, Theme.Border, frame, S(8));

            const TextFormatFlags f = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var lineH = TextRenderer.MeasureText(g, "Ag", Theme.Body).Height;
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var y = i * RowH;
                if (i > 0)
                    using (var p = new Pen(Theme.Border))
                        g.DrawLine(p, S(14), y, Width - S(14), y);

                var icon = new RectangleF(S(14), y + (RowH - S(16)) / 2f, S(16), S(16));
                switch (item.Glyph)
                {
                    case ItemGlyph.Folder: Theme.DrawFolder(g, Theme.Accent, icon); break;
                    case ItemGlyph.File: Theme.DrawFile(g, Theme.Muted, icon); break;
                    default: Theme.DrawFile(g, Theme.DangerText, icon); break;
                }

                var trailingW = item.Trailing.Length > 0 ? S(90) : 0;
                var x = (int)icon.Right + S(12);
                var textW = Width - x - trailingW - S(16);
                var top = y + (RowH - lineH * 2) / 2;

                TextRenderer.DrawText(g, item.Path, Theme.Body, new Rectangle(x, top, textW, lineH), Theme.Text,
                    f | TextFormatFlags.PathEllipsis);
                TextRenderer.DrawText(g, item.Detail, Theme.Small, new Rectangle(x, top + lineH, textW, lineH),
                    item.Glyph == ItemGlyph.Error ? Theme.DangerText : Theme.Muted, f | TextFormatFlags.EndEllipsis);

                if (trailingW > 0)
                    TextRenderer.DrawText(g, item.Trailing, Theme.Strong,
                        new Rectangle(Width - trailingW - S(14), y, trailingW, RowH), Theme.Text,
                        f | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
