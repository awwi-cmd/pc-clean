namespace PcClean;

public sealed class DeleteOutcome
{
    public List<FileNode> DeletedNodes { get; } = new();
    public List<string> Errors { get; } = new();
    public long Freed { get; set; }
    public bool Cancelled { get; set; }

    public int Deleted => DeletedNodes.Count;
}

public sealed class DeleteProgress
{
    public long BytesDone { get; set; }
    public long TotalBytes { get; set; }
    public long FilesDone { get; set; }
    public string Current { get; set; } = "";
}

public static class DeleteService
{
    public static List<FileNode> CollectChecked(FileNode root)
    {
        var list = new List<FileNode>();
        if (root.Checked)
        {
            list.Add(root);
            return list;
        }

        Collect(root, list);
        return list;
    }

    static void Collect(FileNode node, List<FileNode> list)
    {
        foreach (var child in node.Children)
        {
            if (child.Checked)
                list.Add(child);
            else if (child.IsDirectory)
                Collect(child, list);
        }
    }

    public static DeleteOutcome Delete(IReadOnlyList<FileNode> targets, IProgress<DeleteProgress>? progress, CancellationToken ct)
    {
        var outcome = new DeleteOutcome();
        var report = new DeleteProgress { TotalBytes = targets.Sum(t => t.Size) };

        foreach (var node in targets)
        {
            if (ct.IsCancellationRequested)
            {
                outcome.Cancelled = true;
                break;
            }

            report.Current = node.FullPath;
            progress?.Report(report);

            try
            {
                if (node.IsDirectory)
                    DeleteDirectory(node.FullPath, report, progress, ct);
                else
                    DeleteFile(node.FullPath, report, progress);

                outcome.Freed += node.Size;
                outcome.DeletedNodes.Add(node);
                Detach(node);
            }
            catch (OperationCanceledException)
            {
                outcome.Cancelled = true;
                break;
            }
            catch (Exception ex)
            {
                outcome.Errors.Add($"{node.FullPath}: {ex.Message}");
            }
        }

        return outcome;
    }

    static void DeleteDirectory(string dir, DeleteProgress report, IProgress<DeleteProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var file in Directory.EnumerateFiles(dir))
        {
            ct.ThrowIfCancellationRequested();
            DeleteFile(file, report, progress);
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            ct.ThrowIfCancellationRequested();

            // Never follow reparse points (junctions/symlinks): remove the link
            // only, otherwise we would delete the linked-to target's contents.
            if (IsReparsePoint(sub))
            {
                Directory.Delete(sub);
                continue;
            }

            DeleteDirectory(sub, report, progress, ct);
        }

        Directory.Delete(dir);
    }

    static void DeleteFile(string path, DeleteProgress report, IProgress<DeleteProgress>? progress)
    {
        ClearReadOnly(path);

        long length = 0;
        try { length = new FileInfo(path).Length; } catch { }

        File.Delete(path);

        report.BytesDone += length;
        report.FilesDone++;
        if (report.FilesDone % 32 == 0)
            progress?.Report(report);
    }

    static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch
        {
        }
    }

    static void Detach(FileNode node)
    {
        node.Parent?.Children.Remove(node);
        for (var p = node.Parent; p != null; p = p.Parent)
        {
            p.Size -= node.Size;
            p.FileCount -= node.IsDirectory ? node.FileCount : 1;
        }
        node.Parent = null;
    }
}
