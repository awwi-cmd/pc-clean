using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace PcClean;

static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AttachConsole(int dwProcessId);

    const int AttachParentProcess = -1;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--selftest")
        {
            AttachConsole(AttachParentProcess);
            return SelfTest.Run();
        }

        if (args.Length >= 2 && args[0] == "--cli")
        {
            AttachConsole(AttachParentProcess);
            return Cli.Print(args[1]);
        }

        ApplicationConfiguration.Initialize();

        if (!IsAdmin())
        {
            if (RelaunchElevated())
                return 0;

            ThemedDialog.Inform(null,
                "Administrator rights needed",
                "PC Clean reads and deletes protected files, so Windows has to approve it. Start it again and choose Yes on the prompt.");
            return 1;
        }

        Application.Run(new MainForm());
        return 0;
    }

    static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static bool RelaunchElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory,
            });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
