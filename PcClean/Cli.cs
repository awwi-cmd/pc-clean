namespace PcClean;

public static class Cli
{
    public static int Print(string path)
    {
        path = Util.NormalizeFolder(path);
        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Not found: {path}");
            return 2;
        }

        var result = Scanner.Scan(path, null, CancellationToken.None);
        PrintNode(result.Root, "", true);

        var stats = result.Stats;
        Console.WriteLine();
        Console.WriteLine($"TOTAL {result.Root.Size} bytes ({Util.FormatSize(result.Root.Size)}) | {result.Root.FileCount:N0} files | {stats.Dirs:N0} folders | {stats.Elapsed.TotalSeconds:0.00}s");
        if (stats.SkippedTotal > 0)
            Console.WriteLine($"SKIPPED {stats.SkippedDirs} dirs, {stats.SkippedFiles} files, {stats.SkippedLinks} links");

        Console.Out.Flush();
        return 0;
    }

    static void PrintNode(FileNode node, string indent, bool isRoot)
    {
        if (isRoot)
            Console.WriteLine($"{node.FullPath}  [{node.Size}]");
        else if (node.IsDirectory)
            Console.WriteLine($"{indent}{node.Name}  {Util.FormatSize(node.Size)}  [{node.Size}]  ({node.FileCount:N0} files)");
        else
            Console.WriteLine($"{indent}{node.Name}  {Util.FormatSize(node.Size)}  [{node.Size}]");

        foreach (var child in node.Children)
            PrintNode(child, indent + "  ", false);
    }
}
