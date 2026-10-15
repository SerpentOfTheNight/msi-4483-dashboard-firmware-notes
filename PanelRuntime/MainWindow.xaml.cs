using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using PanelRuntime.Models;
using PanelRuntime.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PanelRuntime;

public partial class MainWindow : Window
{
    private const string StartupValueName = "PanelRuntime";
    private const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly RuntimeConfig _config;
    private readonly string _configPath;
    private readonly string _htmlPath;
    private readonly bool _startHidden;
    private readonly bool _autoStart;
    private RuntimeSupervisor? _runtime;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _showMenuItem;
    private Forms.ToolStripMenuItem? _runMenuItem;
    private Forms.ToolStripMenuItem? _startupMenuItem;
    private bool _closing;
    private bool _allowExit;
    private bool _parkedToTray;
    private Rect? _restoreBoundsBeforeTray;

    public MainWindow() : this(false, false)
    {
    }

    public MainWindow(bool startHidden, bool autoStart)
    {
        InitializeComponent();
        _startHidden = startHidden;
        _autoStart = autoStart;
        _configPath = ResolveFile("appsettings.json");
        _config = RuntimeConfig.Load(_configPath);
        UiStrings.Init(_config.UiLanguage);
        _htmlPath = ResolveFile(_config.PanelHtml);
        ModeText.Text = _config.Mode;
        AidaText.Text = "not started";
        DashboardText.Text = _config.SendToDevice ? "armed by config" : "disabled";
        FrameText.Text = "0";
        ErrorText.Text = "";
        SimClickButton.IsEnabled = false;
        InitializeTrayIcon();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await PanelWebView.EnsureCoreWebView2Async();
            PanelWebView.Source = new Uri(_htmlPath);
            Log($"Config: {_configPath}");
            Log($"Panel:  {_htmlPath}");
            if (_autoStart)
            {
                await StartRuntimeAsync();
            }
            if (_startHidden)
            {
                ParkToTray(false);
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            Log($"WebView2 init failed: {ex.Message}");
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        await StartRuntimeAsync();
    }

    private async Task StartRuntimeAsync()
    {
        if (_runtime is not null)
        {
            return;
        }

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SimClickButton.IsEnabled = true;
        try
        {
            _runtime = new RuntimeSupervisor(_config, Dispatcher, Log, UpdateStatus);
            await _runtime.StartAsync(PanelWebView, _htmlPath);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            Log($"Start failed: {ex.Message}");
            await StopRuntimeAsync();
        }
        finally
        {
            UpdateTrayMenu();
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRuntimeAsync();
    }

    private async void SimClickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runtime is null)
        {
            return;
        }

        await _runtime.SimulateClickAsync(_config.Width / 2.0, _config.Height / 2.0);
        Log("Simulated click at panel center.");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => ParkToTray(true));
            return;
        }

        if (_closing)
        {
            DisposeTrayIcon();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        Dispatcher.InvokeAsync(ExitApplicationAsync);
    }

    private async Task StopRuntimeAsync()
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        if (_runtime is not null)
        {
            await _runtime.StopAsync();
            _runtime = null;
        }
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SimClickButton.IsEnabled = false;
        UpdateTrayMenu();
    }

    private void UpdateStatus(RuntimeStatus status)
    {
        ModeText.Text = status.Mode;
        AidaText.Text = status.Aida64;
        DashboardText.Text = status.Dashboard;
        FrameText.Text = status.Frames;
        ErrorText.Text = status.LastError;
    }

    private void Log(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        });
    }

    private void InitializeTrayIcon()
    {
        _showMenuItem = new Forms.ToolStripMenuItem(UiStrings.T("Show window", "显示窗口"));
        _showMenuItem.Click += ShowMenuItem_Click;

        _runMenuItem = new Forms.ToolStripMenuItem(UiStrings.T("Start runtime", "启动运行"));
        _runMenuItem.Click += (_, _) => Dispatcher.InvokeAsync(async () =>
        {
            if (_runtime is null)
            {
                await StartRuntimeAsync();
            }
            else
            {
                await StopRuntimeAsync();
            }
        });

        _startupMenuItem = new Forms.ToolStripMenuItem(UiStrings.T("Start with Windows", "开机启动"));
        _startupMenuItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            SetStartupEnabled(!IsStartupEnabled());
            UpdateTrayMenu();
        });

        var exitMenuItem = new Forms.ToolStripMenuItem(UiStrings.T("Exit", "退出"));
        exitMenuItem.Click += (_, _) => Dispatcher.InvokeAsync(ExitApplicationAsync);

        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => Dispatcher.Invoke(UpdateTrayMenu);
        menu.Items.Add(_showMenuItem);
        menu.Items.Add(_runMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitMenuItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "PanelRuntime",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        UpdateTrayMenu();
    }

    private Drawing.Icon LoadTrayIcon()
    {
        try
        {
            return new Drawing.Icon(ResolveFile("assets/app/panel-runtime.ico"));
        }
        catch
        {
            return Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? Drawing.SystemIcons.Application;
        }
    }

    private void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        if (_restoreBoundsBeforeTray is { } bounds)
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        _parkedToTray = false;
        Activate();
        UpdateTrayMenu();
    }

    private void ParkToTray(bool showTip)
    {
        if (!_parkedToTray)
        {
            _restoreBoundsBeforeTray = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
        }

        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft - Width - 96;
        Top = SystemParameters.VirtualScreenTop - Height - 96;
        _parkedToTray = true;
        UpdateTrayMenu();
        if (showTip)
        {
            _trayIcon?.ShowBalloonTip(
                1800,
                "PanelRuntime",
                UiStrings.T(
                    "Minimized to the system tray. Right-click the icon to exit or manage startup.",
                    "已缩小到系统托盘，右键图标可退出或设置开机启动。"),
                Forms.ToolTipIcon.Info);
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _allowExit = true;
        await StopRuntimeAsync();
        DisposeTrayIcon();
        System.Windows.Application.Current.Shutdown();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void UpdateTrayMenu()
    {
        if (_showMenuItem is not null)
        {
            _showMenuItem.Text = _parkedToTray
                ? UiStrings.T("Show window", "显示窗口")
                : UiStrings.T("Hide to tray", "隐藏到托盘");
            _showMenuItem.Click -= ShowMenuItem_Click;
            _showMenuItem.Click += ShowMenuItem_Click;
        }
        if (_runMenuItem is not null)
        {
            _runMenuItem.Text = _runtime is null
                ? UiStrings.T("Start runtime", "启动运行")
                : UiStrings.T("Stop runtime", "停止运行");
        }
        if (_startupMenuItem is not null)
        {
            _startupMenuItem.Checked = IsStartupEnabled();
        }
    }

    private void ShowMenuItem_Click(object? sender, EventArgs e)
    {
        if (!_parkedToTray)
        {
            ParkToTray(false);
        }
        else
        {
            ShowFromTray();
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, false);
        return key?.GetValue(StartupValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(StartupKeyPath, true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(StartupValueName, GetStartupCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(StartupValueName, false);
        }
    }

    private static string GetStartupCommand()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            exePath = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");
        }
        return $"\"{exePath}\" --minimized --start";
    }

    private static string ResolveFile(string relativePath)
    {
        var baseCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
        if (File.Exists(baseCandidate))
        {
            return baseCandidate;
        }

        var cwdCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
        if (File.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        var sourceCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "PanelRuntime", relativePath));
        if (File.Exists(sourceCandidate))
        {
            return sourceCandidate;
        }

        throw new FileNotFoundException($"Cannot resolve {relativePath}.", relativePath);
    }
}
