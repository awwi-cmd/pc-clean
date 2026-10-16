using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PcClean;

public sealed class MainForm : Form
{
    const string IdleHint = "Tick folders or files to mark them for deletion. Arrows move, Space ticks.";

    readonly PathField _path = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), Height = 36 };
    readonly FlatButton _browse = new("Browse…", ButtonKind.Secondary) { Margin = new Padding(0, 0, 8, 0) };
    readonly FlatButton _scan = new("Scan", ButtonKind.Primary) { Margin = Padding.Empty };
    readonly FlatButton _cancel = new("Cancel", ButtonKind.Secondary) { Margin = Padding.Empty, Visible = false };
    readonly FlatButton _delete = new("Delete", ButtonKind.Danger) { Enabled = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
    readonly ProgressLine _progress = new() { Dock = DockStyle.Top, Height = 2 };
    readonly SummaryStrip _summary = new() { Dock = DockStyle.Top, Height = 84 };
    readonly SizeTree _tree = new() { Dock = DockStyle.Fill };
    readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        UseMnemonic = false,
        ForeColor = Theme.Muted,
        Margin = Padding.Empty,
    };

    FileNode? _root;
    ScanStats? _stats;
    CancellationTokenSource? _cts;
    bool _busy;
    string? _scanNote;
    string? _notice;
    Color _noticeColor;

    public MainForm()
    {
        Text = "PC Clean";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = Theme.Body;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        ClientSize = new Size(1080, 720);
        MinimumSize = new Size(760, 480);
        StartPosition = FormStartPosition.CenterScreen;

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(20, 14, 20, 14),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.Controls.Add(new Label
        {
            Text = "PC Clean",
            Font = Theme.Wordmark,
            ForeColor = Theme.Text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 20, 0),
        }, 0, 0);
        toolbar.Controls.Add(_path, 1, 0);
        toolbar.Controls.Add(_browse, 2, 0);
        toolbar.Controls.Add(_scan, 3, 0);
        toolbar.Controls.Add(_cancel, 4, 0);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = Theme.Surface };
        var footerRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(20, 12, 20, 12),
        };
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        footerRow.Controls.Add(_status, 0, 0);
        footerRow.Controls.Add(_delete, 1, 0);
        footer.Controls.Add(footerRow);
        footer.Controls.Add(_progress);

        Controls.Add(_tree);
        Controls.Add(footer);
        Controls.Add(_summary);
        Controls.Add(toolbar);

        _tree.EmptyTitle = "Find what's filling your disk";
        _tree.EmptyHint = "Choose a folder or a whole drive above and press Scan. The biggest items rise to the top — tick what you want gone, then Delete.";
        _summary.SetHeadline("No folder scanned", "Sizes are exact byte counts read from disk, hidden and system files included.");
        _status.Text = "Ready.";

        _browse.Click += (_, _) => Browse();
        _scan.Click += async (_, _) => await ScanAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _delete.Click += async (_, _) => await DeleteSelectedAsync();
        _path.Box.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            await ScanAsync();
        };
        _tree.CheckToggled += node =>
        {
            CheckLogic.SetChecked(node, !node.Checked);
            _notice = null;
            UpdateStatus();
        };
        FormClosing += (_, _) => _cts?.Cancel();
        Shown += (_, _) => _path.Box.Focus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }

    void Browse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Choose a folder or drive to scan",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_path.Box.Text) ? _path.Box.Text : string.Empty,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _path.Box.Text = dlg.SelectedPath;
    }

    async Task ScanAsync()
    {
        if (_busy)
            return;

        var path = Util.NormalizeFolder(_path.Box.Text);
        var problem =
            path.Length == 0 ? "Type a folder path or use Browse… to pick one." :
            File.Exists(path) ? $"{path} is a file. Enter a folder, or a drive root such as C:\\." :
            !Directory.Exists(path) ? $"Couldn't find {path}. Check the spelling, or pick it with Browse…" :
            null;
        if (problem is not null)
        {
            _summary.SetHeadline("Can't scan that", problem, isError: true);
            _path.Box.Focus();
            _path.Box.SelectAll();
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        _root = null;
        _stats = null;
        _scanNote = null;
        _notice = null;
        _tree.Root = null;
        _tree.EmptyTitle = "Scanning…";
        _tree.EmptyHint = "Reading every file, including hidden and system ones.";
        _summary.Selected = 0;
        _summary.SetDrive(Util.GetVolume(path));
        _summary.SetHeadline(DisplayName(path), $"{path} · scanning…");
        _status.ForeColor = Theme.Muted;
        _status.Text = $"Scanning {path}";

        _progress.StartIndeterminate();
        SetBusy(true);

        var progress = new Progress<ScanStats>(s =>
        {
            _tree.EmptyHint = $"{s.Files:N0} files · {Util.FormatSize(s.Bytes)} · {s.Dirs:N0} folders so far";
            _tree.Invalidate();
        });

        try
        {
            var sw = Stopwatch.StartNew();
            var result = await Task.Run(() => Scanner.Scan(path, progress, cts.Token), cts.Token);
            sw.Stop();

            _root = result.Root;
            _stats = result.Stats;
            _scanNote = $"scanned in {sw.Elapsed.TotalSeconds:0.0} s";
            _tree.Root = result.Root;
            ShowRootHeadline();
        }
        catch (OperationCanceledException)
        {
            _summary.SetHeadline("Scan cancelled", $"{path} · nothing was changed.");
            _tree.EmptyTitle = "Scan cancelled";
            _tree.EmptyHint = "Press Scan to start again.";
        }
        catch (Exception ex)
        {
            _summary.SetHeadline("Scan failed", ex.Message, isError: true);
            _tree.EmptyTitle = "Scan failed";
            _tree.EmptyHint = "Check that the folder is still there and readable, then press Scan again.";
        }
        finally
        {
            cts.Dispose();
            if (_cts == cts)
                _cts = null;
            _progress.Stop();
            SetBusy(false);
        }

        if (_root is not null)
            _tree.Focus();
    }

    static string DisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? $"Drive {path.TrimEnd('\\')}" : name;
    }

    void ShowRootHeadline()
    {
        if (_root is null)
            return;

        var detail = $"{_root.FullPath} · {Util.FormatSize(_root.Size)} · {Util.Count(_root.FileCount, "file")}";
        if (_stats is { } stats)
        {
            detail += $" · {Util.Count(stats.Dirs, "folder")}";
            if (stats.SkippedTotal > 0)
                detail += $" · {stats.SkippedTotal:N0} skipped";
        }
        if (_scanNote is { } note)
            detail += $" · {note}";
        _summary.SetHeadline(DisplayName(_root.FullPath), detail);
    }

    async Task DeleteSelectedAsync()
    {
        if (_busy || _root is null)
            return;

        var targets = DeleteService.CollectChecked(_root);
        if (targets.Count == 0)
            return;

        long size = targets.Sum(t => t.Size);
        int files = targets.Sum(t => t.IsDirectory ? t.FileCount : 1);

        const int maxItems = 8;
        var ordered = targets.OrderByDescending(t => t.Size).ToList();
        var items = ordered.Take(maxItems).Select(t => new DialogItem(
            t.IsDirectory ? ItemGlyph.Folder : ItemGlyph.File,
            t.FullPath,
            t.IsDirectory ? $"Folder · {Util.Count(t.FileCount, "file")}" : "File",
            Util.FormatSize(t.Size))).ToList();
        string? overflow = null;
        if (ordered.Count > maxItems)
        {
            var rest = ordered.Skip(maxItems).ToList();
            overflow = $"+ {Util.Count(rest.Count, "more item")} · {Util.FormatSize(rest.Sum(t => t.Size))}";
        }

        var confirmed = ThemedDialog.Confirm(this,
            $"Permanently delete {Util.Count(targets.Count, "item")}?",
            $"{Util.FormatSize(size)} in {Util.Count(files, "file")} will be removed from disk.",
            items, overflow,
            "This skips the Recycle Bin and can't be undone.",
            $"Delete {Util.FormatSize(size)}");
        if (!confirmed)
            return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        _progress.StartDeterminate();
        SetBusy(true);
        _status.ForeColor = Theme.Muted;

        var progress = new Progress<DeleteProgress>(dp =>
        {
            _progress.Value = dp.TotalBytes > 0 ? (float)dp.BytesDone / dp.TotalBytes : 0f;
            _status.Text = $"Deleting · {Util.FormatSize(dp.BytesDone)} of {Util.FormatSize(dp.TotalBytes)} · {dp.Current}";
        });

        DeleteOutcome outcome;
        try
        {
            outcome = await Task.Run(() => DeleteService.Delete(targets, progress, cts.Token), cts.Token);
        }
        finally
        {
            cts.Dispose();
            if (_cts == cts)
                _cts = null;
            _progress.Stop();
            SetBusy(false);
        }

        var freed = Util.FormatSize(outcome.Freed);
        var deleted = Util.Count(outcome.Deleted, "item");
        var root = _root;
        _summary.SetDrive(Util.GetVolume(root.FullPath));

        if (outcome.DeletedNodes.Contains(root))
        {
            _root = null;
            _stats = null;
            _scanNote = null;
            _tree.Root = null;
            _tree.EmptyTitle = "Folder deleted";
            _tree.EmptyHint = "Scan another folder or drive to keep going.";
            _summary.SetHeadline(DisplayName(root.FullPath), $"{root.FullPath} was deleted · freed {freed}.");
            SetNotice($"Freed {freed} · deleted {deleted}.", Theme.Success);
        }
        else
        {
            _tree.Reload();
            ShowRootHeadline();
            if (outcome.Cancelled)
                SetNotice($"Delete cancelled after freeing {freed} ({deleted}). Rescan for exact sizes.", Theme.Accent);
            else
                SetNotice($"Freed {freed} · deleted {deleted}.", Theme.Success);
        }

        if (outcome.Errors.Count > 0)
        {
            const int maxErrors = 8;
            var errorItems = outcome.Errors.Take(maxErrors).Select(err =>
            {
                // Errors read "<path>: <message>"; a path can't contain ':' past the drive letter.
                var split = err.IndexOf(": ", 2, StringComparison.Ordinal);
                return split < 0
                    ? new DialogItem(ItemGlyph.Error, err, "")
                    : new DialogItem(ItemGlyph.Error, err[..split], err[(split + 2)..]);
            }).ToList();
            ThemedDialog.Inform(this,
                $"{Util.Count(outcome.Errors.Count, "item")} couldn't be deleted",
                "Everything else was removed. These were left in place:",
                errorItems,
                outcome.Errors.Count > maxErrors ? $"+ {Util.Count(outcome.Errors.Count - maxErrors, "more item")}" : null);
        }
    }

    void SetNotice(string text, Color color)
    {
        _notice = text;
        _noticeColor = color;
        UpdateStatus();
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        _path.Enabled = !busy;
        _browse.Enabled = !busy;
        _scan.Visible = !busy;
        _cancel.Visible = busy;
        _tree.Enabled = !busy;

        if (busy)
        {
            _delete.Enabled = false;
            _cancel.Focus();
        }
        else
        {
            UpdateStatus();
        }
    }

    void UpdateStatus()
    {
        if (_root is null)
        {
            _delete.Enabled = false;
            _delete.Text = "Delete";
            _summary.Selected = 0;
            if (_notice is not null)
            {
                _status.ForeColor = _noticeColor;
                _status.Text = _notice;
            }
            return;
        }

        var selected = DeleteService.CollectChecked(_root);
        long selectedSize = selected.Sum(n => n.Size);

        // Folders that hold ticked items get a partial mark in the tree.
        var partial = new HashSet<FileNode>();
        foreach (var node in selected)
            for (var p = node.Parent; p is not null && partial.Add(p); p = p.Parent) { }
        _tree.SetPartial(partial);
        _summary.Selected = selectedSize;

        if (_notice is not null)
        {
            _status.ForeColor = _noticeColor;
            _status.Text = _notice;
        }
        else if (selected.Count > 0)
        {
            var files = selected.Sum(n => n.IsDirectory ? n.FileCount : 1);
            _status.ForeColor = Theme.Text;
            _status.Text = $"{Util.Count(selected.Count, "item")} marked · {Util.FormatSize(selectedSize)} in {Util.Count(files, "file")}";
        }
        else
        {
            _status.ForeColor = Theme.Muted;
            _status.Text = IdleHint;
        }

        _delete.Text = selected.Count > 0 ? $"Delete {Util.FormatSize(selectedSize)}" : "Delete";
        _delete.Enabled = !_busy && selected.Count > 0;
    }
}
