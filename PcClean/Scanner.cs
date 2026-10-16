using System.Diagnostics;
using System.Security;

namespace PcClean;

public sealed class FileNode
{
    public FileNode(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        var trimmed = fullPath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        Name = string.IsNullOrEmpty(name) ? fullPath : name;
        IsDirectory = isDirectory;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }

    public long Size { get; set; }
    public int FileCount { get; set; }
    public bool Checked { get; set; }

    public FileNode? Parent { get; set; }
    public List<FileNode> Children { get; } = new();

    internal DirectoryInfo? Dir { get; set; }
}

public sealed class ScanStats
{
    public long Files;
    public long Dirs;
    public long Bytes;
    public long SkippedDirs;
    public long SkippedFiles;
    public long SkippedLinks;
    public TimeSpan Elapsed;

    public long SkippedTotal => SkippedDirs + SkippedFiles + SkippedLinks;
}

public sealed class ScanResult
{
    public required FileNode Root { get; init; }
    public required ScanStats Stats { get; init; }
}

public static class Scanner
{
    static readonly EnumerationOptions Opts = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static ScanResult Scan(string path, IProgress<ScanStats>? progress, CancellationToken ct)
    {
        var di = new DirectoryInfo(path);
        if (!di.Exists)
            throw new DirectoryNotFoundException($"Folder not found: {path}");

        var stats = new ScanStats();
        var sw = Stopwatch.StartNew();
        var root = new FileNode(di.FullName, true) { Dir = di };
        ScanDir(root, stats, progress, sw, ct);
        stats.Elapsed = sw.Elapsed;
        progress?.Report(stats);
        return new ScanResult { Root = root, Stats = stats };
    }

    static void ScanDir(FileNode node, ScanStats stats, IProgress<ScanStats>? progress, Stopwatch sw, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        FileSystemInfo[] entries;
        try
        {
            entries = node.Dir!.EnumerateFileSystemInfos("*", Opts).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            stats.SkippedDirs++;
            return;
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry is FileInfo fi)
            {
                try
                {
                    var len = fi.Length;
                    node.Children.Add(new FileNode(fi.FullName, false) { Size = len, Parent = node });
                    stats.Files++;
                    stats.Bytes += len;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    stats.SkippedFiles++;
                }
            }
            else if (entry is DirectoryInfo sub)
            {
                FileAttributes attrs;
                try
                {
                    attrs = sub.Attributes;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    stats.SkippedDirs++;
                    continue;
                }

                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    stats.SkippedLinks++;
                    continue;
                }

                var child = new FileNode(sub.FullName, true) { Dir = sub, Parent = node };
                ScanDir(child, stats, progress, sw, ct);
                node.Children.Add(child);
                stats.Dirs++;
            }

            if (stats.Files % 500 == 0 || stats.Dirs % 200 == 0)
            {
                stats.Elapsed = sw.Elapsed;
                progress?.Report(stats);
            }
        }

        foreach (var child in node.Children)
        {
            node.Size += child.Size;
            node.FileCount += child.IsDirectory ? child.FileCount : 1;
        }

        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
    }
}
