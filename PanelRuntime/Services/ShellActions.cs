using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PanelRuntime.Services;

public static class ShellActions
{
    private const byte VK_LWIN = 0x5B;
    private const byte VK_D = 0x44;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

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
                SendWinD();
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

/// <summary>Same as pressing Win+D (toggle show desktop).</summary>
    private static void SendWinD()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_D, 0, 0, UIntPtr.Zero);
        keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}