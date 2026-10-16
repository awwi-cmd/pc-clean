using System.Diagnostics;

namespace PcClean;

public static class SelfTest
{
    static int _failures;

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "pc-clean-selftest");
        Cleanup(root);

        try
        {
            Setup(root);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SETUP FAILED: {ex.Message}");
            return 1;
        }

        try
        {
            var result = Scanner.Scan(root, null, CancellationToken.None);
            var rootNode = result.Root;
            var stats = result.Stats;

            const long f1 = 1_000_000;
            const long f2 = 2_500_000;
            const long f3 = 700_000;
            const long rootFile = 1_234_567;
            const long expectedTotal = f1 + f2 + f3 + rootFile;

            Check(rootNode.Size == expectedTotal, $"total size {rootNode.Size} == {expectedTotal}");
            Check(rootNode.FileCount == 4, $"total file count {rootNode.FileCount} == 4");
            Check(stats.Dirs == 2, $"scanned folders {stats.Dirs} == 2");
            Check(stats.SkippedLinks >= 1, $"junction skipped (links={stats.SkippedLinks})");
            Check(stats.SkippedDirs == 0, $"no skipped folders (got {stats.SkippedDirs})");

            var dirA = rootNode.Children.First(c => c.Name == "a");
            var dirB = rootNode.Children.First(c => c.Name == "b");
            var rootBin = rootNode.Children.First(c => c.Name == "root.bin");

            Check(dirA.Size == f1 + f2, $"folder a size {dirA.Size} == {f1 + f2}");
            Check(dirA.FileCount == 2, $"folder a file count {dirA.FileCount} == 2");
            Check(dirB.Size == f3, $"folder b size {dirB.Size} == {f3}");

            var sortedDesc = true;
            for (var i = 0; i + 1 < rootNode.Children.Count; i++)
                sortedDesc &= rootNode.Children[i].Size >= rootNode.Children[i + 1].Size;
            Check(sortedDesc, "children sorted descending by size");

            var parent = new FileNode("C:\\p", true);
            var onlyChild = new FileNode("C:\\p\\only", true) { Parent = parent };
            parent.Children.Add(onlyChild);

            CheckLogic.SetChecked(parent, true);
            Check(parent.Checked && onlyChild.Checked, "checking a folder checks its children");

            CheckLogic.SetChecked(onlyChild, false);
            Check(parent.Checked == false, "unchecking a child clears its parent");

            CheckLogic.SetChecked(onlyChild, true);
            Check(parent.Checked == false, "checking a child does not check its parent");
            var onlyTargets = DeleteService.CollectChecked(parent);
            Check(onlyTargets.Count == 1 && onlyTargets[0] == onlyChild, "only the checked child is a delete target");

            foreach (var child in dirA.Children)
                child.Checked = true;
            dirA.Checked = true;
            rootBin.Checked = true;

            var targets = DeleteService.CollectChecked(rootNode);
            Check(targets.Count == 2, $"collect returns 2 top-most targets (got {targets.Count})");
            Check(targets.Contains(dirA) && targets.Contains(rootBin), "collect keeps only folder a and root.bin");

            var outcome = DeleteService.Delete(targets, null, CancellationToken.None);
            Check(outcome.Errors.Count == 0, $"no delete errors ({string.Join("; ", outcome.Errors)})");
            Check(outcome.Deleted == 2, $"deleted 2 items (got {outcome.Deleted})");
            Check(!Directory.Exists(dirA.FullPath), "folder a removed from disk");
            Check(!File.Exists(rootBin.FullPath), "root.bin removed from disk");

            Check(rootNode.Size == f3, $"model size after delete {rootNode.Size} == {f3}");
            Check(rootNode.FileCount == 1, $"model file count after delete {rootNode.FileCount} == 1");
            Check(rootNode.Children.Count == 1, "model root has 1 child after delete");

            var rescan = Scanner.Scan(root, null, CancellationToken.None);
            Check(rescan.Root.Size == f3, $"rescan size {rescan.Root.Size} == {f3}");

            // recursive delete must remove a junction link, never its target
            var safety = Path.Combine(Path.GetTempPath(), "pc-clean-selftest-del");
            RemoveTree(safety);
            Directory.CreateDirectory(Path.Combine(safety, "keep"));
            WriteFile(Path.Combine(safety, "keep", "keep.bin"), 100);
            Directory.CreateDirectory(Path.Combine(safety, "delme"));
            WriteFile(Path.Combine(safety, "delme", "f.bin"), 200);
            RunCmd($"/c mklink /J \"{Path.Combine(safety, "delme", "lnk")}\" \"{Path.Combine(safety, "keep")}\"");

            var delTarget = new FileNode(Path.Combine(safety, "delme"), true) { Size = 200, FileCount = 1 };

            using (var preCancelled = new CancellationTokenSource())
            {
                preCancelled.Cancel();
                var pre = DeleteService.Delete(new[] { delTarget }, null, preCancelled.Token);
                Check(pre.Cancelled && pre.Deleted == 0 && Directory.Exists(delTarget.FullPath),
                    "pre-cancelled delete leaves everything in place");
            }

            var volume = Util.GetVolume(delTarget.FullPath);
            Check(volume is not null && volume.TotalSize > 0, "volume free/total space readout works");

            var delOutcome = DeleteService.Delete(new[] { delTarget }, null, CancellationToken.None);
            Check(delOutcome.Errors.Count == 0, $"junction folder deleted without errors ({string.Join("; ", delOutcome.Errors)})");
            Check(!Directory.Exists(delTarget.FullPath), "folder containing a junction was deleted");
            Check(File.Exists(Path.Combine(safety, "keep", "keep.bin")), "junction target was left untouched");
            RemoveTree(safety);
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"EXCEPTION: {ex}");
        }
        finally
        {
            Cleanup(root);
        }

        Console.WriteLine(_failures == 0 ? "ALL TESTS PASSED" : $"{_failures} TEST(S) FAILED");
        Console.Out.Flush();
        return _failures == 0 ? 0 : 1;
    }

    static void Setup(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        WriteFile(Path.Combine(root, "root.bin"), 1_234_567);
        WriteFile(Path.Combine(root, "a", "f1.bin"), 1_000_000);
        WriteFile(Path.Combine(root, "a", "f2.bin"), 2_500_000);
        WriteFile(Path.Combine(root, "b", "f3.bin"), 700_000);

        var junction = Path.Combine(root, "loop");
        RunCmd($"/c mklink /J \"{junction}\" \"{root}\"");

        var attrs = new DirectoryInfo(junction).Attributes;
        if ((attrs & FileAttributes.ReparsePoint) == 0)
            throw new InvalidOperationException("failed to create test junction");
    }

    static void WriteFile(string path, long length)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.SetLength(length);
    }

    static void Cleanup(string root)
    {
        var junction = Path.Combine(root, "loop");
        try
        {
            if (Directory.Exists(junction))
                RunCmd($"/c rmdir \"{junction}\"");
        }
        catch
        {
        }

        if (Directory.Exists(root))
            RunCmd($"/c rmdir /s /q \"{root}\"");
    }

    static void RemoveTree(string path)
    {
        if (Directory.Exists(path))
            RunCmd($"/c rmdir /s /q \"{path}\"");
    }

    static void RunCmd(string args)
    {
        var psi = new ProcessStartInfo("cmd.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }

    static void Check(bool condition, string what)
    {
        Console.WriteLine((condition ? "  ok   " : "  FAIL ") + what);
        if (!condition)
            _failures++;
    }
}
