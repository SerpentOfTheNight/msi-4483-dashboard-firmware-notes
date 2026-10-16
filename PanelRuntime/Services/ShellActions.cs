using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PanelRuntime.Services;

public static class ShellActions
{
    public static void Run(string command)
    {
        switch (command)
        {
            case "calc":
            case "calculator":
                Start("calc.exe");
                break;

            case "browser":
            case "web":
                // default browser, new window to home/new tab page
                Start("https://");
                break;

            case "desktop":
            case "showdesktop":
                ToggleDesktop();
                break;

            default:
                throw new InvalidOperationException($"Unknown shell command: {command}");
        }
    }

    private static void Start(string fileName)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        });
    }

    private static void ToggleDesktop()
    {
        // Same as Win+D via Shell.Application
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        shell.ToggleDesktop();
        Marshal.FinalReleaseComObject(shell);
    }
}