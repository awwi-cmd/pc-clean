namespace PcClean;

public static class Util
{
    /// <summary>
    /// Cleans a user-entered folder path without breaking volume roots.
    /// "L:" and "L:\" both become "L:\" (a bare "L:" is drive-relative and would
    /// otherwise resolve to the current directory on that drive, not its root).
    /// </summary>
    public static string NormalizeFolder(string input)
    {
        var path = input.Trim();
        if (path.Length == 0)
            return path;

        var root = Path.GetPathRoot(path);
        var trimmed = path.TrimEnd('\\', '/');

        if (trimmed.Length == 0)
            return root ?? path;                           // "\" -> root of current drive
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + Path.DirectorySeparatorChar;  // "L:" -> "L:\"
        return trimmed;
    }

    public static DriveInfo? GetVolume(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return null;

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive : null;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int i = -1;
        do
        {
            value /= 1024;
            i++;
        } while (value >= 1024 && i < units.Length - 1);

        // Three significant digits keeps columns steady: 1.53 GB, 15.3 GB, 153 GB.
        var format = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return $"{value.ToString(format)} {units[i]}";
    }

    /// <summary>"1 file", "2,048 files".</summary>
    public static string Count(long n, string noun) => n == 1 ? $"1 {noun}" : $"{n:N0} {noun}s";
}
