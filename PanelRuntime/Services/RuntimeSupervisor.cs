using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PanelRuntime.Models;

namespace PanelRuntime.Services;

public sealed record RuntimeStatus(
    string Mode,
    string Aida64,
    string Dashboard,
    string Frames,
    string LastError);

public sealed class RuntimeSupervisor : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null
    };

    private readonly RuntimeConfig _config;
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _log;
    private readonly Action<RuntimeStatus> _status;
    private readonly Aida64SensorProvider _aida64 = new();
    private readonly WindowsNetworkProvider _network;
    private readonly WindowsStorageProvider _storage;

    private readonly MediaSessionProvider _mediaSession ;
    private DispatcherTimer? _mediaTimer;
    private long _lastMediaDispatchUtcMs;
    private readonly Stopwatch _wallClock = new();
    private readonly object _frameSignalLock = new();

    private CancellationTokenSource? _cts;
    private Task? _sensorTask;
    private Task? _frameTask;
    private TaskCompletionSource _frameSignal = NewFrameSignal();
    private WebView2? _webView;
    private FrameCaptureService? _capture;
    private DashboardTransport? _transport;
    private DashboardTouchInputService? _touchInput;
    private bool _transportStarted;
    private long _activeUntilUtcMs;
    private long _lastActivityLogUtcMs;
    private long _lastStorageDispatchUtcMs;
    private long _frames;
    private double _lastCaptureMs;
    private double _lastConvertMs;
    private double _lastSendMs;
    private SensorSnapshot? _lastSnapshot;
    private string _lastError = "";

    private readonly WeatherService _weather;
    private readonly RssFeedService _rss;
    private DispatcherTimer? _weatherTimer;
    private DispatcherTimer? _rssTimer;

    public RuntimeSupervisor(
        RuntimeConfig config,
        Dispatcher dispatcher,
        Action<string> log,
        Action<RuntimeStatus> status)
    {
        _config = config;
        _dispatcher = dispatcher;
        _log = log;
        _status = status;
        _network = new WindowsNetworkProvider(config.NetworkAdapters);
        _storage = new WindowsStorageProvider(TimeSpan.FromMilliseconds(Math.Max(config.StorageHealthPollMs, 10000)));
        _mediaSession = new MediaSessionProvider(dispatcher);
        _weather = new WeatherService(config);
        _rss = new RssFeedService(config);
    }

    public bool IsRunning => _cts is not null;

    public async Task StartAsync(WebView2 webView, string htmlPath)
    {
        if (_cts is not null)
        {
            return;
        }

        if (!File.Exists(htmlPath))
        {
            throw new FileNotFoundException("Panel HTML not found.", htmlPath);
        }

        _webView = webView;
        _capture = new FrameCaptureService(webView, _config.Width, _config.Height);
        _transport = _config.SendToDevice && !IsPreviewOnly()
            ? new DashboardTransport(_log)
            : null;

        await LoadPanelAsync(webView, htmlPath);
        await PrimePanelDataAsync();
        await OnUiAsync(() => 
        {   
            _mediaSession.EnsureAsync();
            StartMediaTimer();
            return Task.CompletedTask;
        });
        StartInfoTimers();

        _cts = new CancellationTokenSource();
        _wallClock.Restart();
        _frames = 0;
        _lastError = "";
        PublishStatus();

        _sensorTask = Task.Run(() => SensorLoopAsync(_cts.Token));
        _frameTask = Task.Run(() => FrameLoopAsync(_cts.Token));
        _log($"Runtime started: mode={_config.Mode}, sendToDevice={_config.SendToDevice}, idleFps={IdleFps:0.###}, activeFps={ActiveFps:0.###}");
    }

    public async Task StopAsync()
    {
        // In StopAsync, before canceling tasks / after cancel:
        StopInfoTimers();
        await OnUiAsync(() =>
        {
            StopMediaTimer();
            return Task.CompletedTask;
        });
        var cts = _cts;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        if (_touchInput is not null)
        {
            await _touchInput.StopAsync();
            _touchInput = null;
        }

        await WhenAllQuietly(_sensorTask, _frameTask);
        _sensorTask = null;
        _frameTask = null;

        if (_transport is not null)
        {
            await _transport.CleanupAsync();
            _transport = null;
        }

        await DetachWebMessageHandlerAsync();

        cts.Dispose();
        _cts = null;
        _transportStarted = false;
        _wallClock.Stop();
        PublishStatus();
        _log("Runtime stopped.");
    }

    private async Task LoadPanelAsync(WebView2 webView, string htmlPath)
{
    await OnUiAsync(async () =>
    {
        await webView.EnsureCoreWebView2Async();
        var core = webView.CoreWebView2;

        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        core.WebMessageReceived -= WebView_WebMessageReceived;
        core.WebMessageReceived += WebView_WebMessageReceived;

        // --- Virtual hosts MUST be set before any navigation ---
        var panelDir = Path.GetDirectoryName(Path.GetFullPath(htmlPath))
                       ?? throw new InvalidOperationException("Invalid panel html path.");
        const string appHost = "panel-app.local";

        try { core.ClearVirtualHostNameToFolderMapping(appHost); } catch { /* ignore */ }
        core.SetVirtualHostNameToFolderMapping(
            appHost,
            panelDir,
            CoreWebView2HostResourceAccessKind.Allow);
        _log($"Panel mapped: http://{appHost}/ → {panelDir}");

        var mediaHost = string.IsNullOrWhiteSpace(_config.MediaVirtualHost)
            ? "panel-media.local"
            : _config.MediaVirtualHost.Trim();

        if (!string.IsNullOrWhiteSpace(_config.MediaFolder))
        {
            var mediaDir = Path.GetFullPath(_config.MediaFolder);
            if (Directory.Exists(mediaDir))
            {
                try { core.ClearVirtualHostNameToFolderMapping(mediaHost); } catch { /* ignore */ }
                core.SetVirtualHostNameToFolderMapping(
                    mediaHost,
                    mediaDir,
                    CoreWebView2HostResourceAccessKind.Allow);
                _log($"Media mapped: http://{mediaHost}/ → {mediaDir}");
            }
            else
            {
                _log($"Media folder missing: {mediaDir}");
            }
        }

        // --- Navigate via virtual host (NOT file://) ---
        var panelUrl = $"http://{appHost}/index.html";
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            core.NavigationCompleted -= Handler;
            if (args.IsSuccess)
                tcs.TrySetResult();
            else
                tcs.TrySetException(new InvalidOperationException(
                    $"WebView2 navigation failed: {args.WebErrorStatus}"));
        }

        core.NavigationCompleted += Handler;
        webView.Source = new Uri(panelUrl);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // List only after page is on http://panel-app.local/
        await DispatchMediaListAsync();
    });
}

    private async Task DispatchMediaListAsync()
    {
        if (_webView?.CoreWebView2 is null || string.IsNullOrWhiteSpace(_config.MediaFolder))
        {
           _log($"No Media found as path is null");
            return;
        }
            

        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".mp4", ".webm", ".mov" };

        var files = Directory.EnumerateFiles(_config.MediaFolder)
            .Where(f => exts.Contains(Path.GetExtension(f)))
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f)
            .ToList();

        _log($"Media folder: '{_config.MediaFolder}' exists={Directory.Exists(_config.MediaFolder ?? "")}");

        // after building the list:
        _log($"Media files found: {files.Count}");
        //_log($"Activity: {source}; active refresh for {holdMs} ms.");
        foreach (var f in files.Take(10)) _log($"  - {f}");

        var hostName = string.IsNullOrWhiteSpace(_config.MediaVirtualHost)
            ? "panel-media.local"
            : _config.MediaVirtualHost.Trim();

        var payload = new
        {
            host = $"http://{hostName}/",   // http, not https
            intervalMs = Math.Max(2000, _config.MediaIntervalMs),
            files
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await OnUiAsync(() =>
            _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.dispatchEvent(new CustomEvent('media-list', {{ detail: {json} }}));"));
    }

    private Task DispatchMediaSessionAsync(MediaSessionSnapshot snap)
{
    if (_webView?.CoreWebView2 is null) return Task.CompletedTask;
    var json = JsonSerializer.Serialize(snap, JsonOptions);
    return OnUiAsync(() =>
        _webView!.CoreWebView2!.ExecuteScriptAsync(
            $"window.dispatchEvent(new CustomEvent('media-session', {{ detail: {json} }}));"));
}

// extend WebView_WebMessageReceived:
private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
{
    var message = args.TryGetWebMessageAsString() ?? "";
    if (message.Contains("rendered", StringComparison.OrdinalIgnoreCase))
    {
        SignalFrameLoop();
    }
    else if (message.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
    {
        var cmd = message["media:".Length..].Trim();
        _ = OnUiAsync(async () =>
        {
            try { await _mediaSession.CommandAsync(cmd); }
            catch (Exception ex) { _log($"Media cmd '{cmd}': {ex.Message}"); }
        });
        NotifyUserActivity("media-cmd");
    }
    else if (message.StartsWith("volume:", StringComparison.OrdinalIgnoreCase))
    {
        var cmd = message["volume:".Length..].Trim();
        try
        {
            _log($"Volume cmd='{cmd}'");
            SystemVolume.Apply(cmd);
            var (level, muted) = SystemVolume.GetState();
            _log($"Volume now level={level:0.00} muted={muted}");
            _ = DispatchSystemVolumeAsync(level, muted);
        }
        catch (Exception ex)
        {
            _log($"Volume: {ex.Message}");
        }
        NotifyUserActivity("volume");
    }

    else if (message.StartsWith("alarm:", StringComparison.OrdinalIgnoreCase))
    {
        var cmd = message["alarm:".Length..].Trim().ToLowerInvariant();
        if (cmd == "play")
            AlarmAudio.Play(_config.AlarmSoundPath);
        else if (cmd == "stop")
            AlarmAudio.Stop();
        NotifyUserActivity("alarm");
    }else if (message.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
    {
        var cmd = message["shell:".Length..].Trim().ToLowerInvariant();
        try
        {
            ShellActions.Run(cmd);
            _log($"Shell: {cmd}");
        }
        catch (Exception ex)
        {
            _log($"Shell '{cmd}': {ex.Message}");
        }
        NotifyUserActivity("shell");
    }else if (message.Contains("activity", StringComparison.OrdinalIgnoreCase))
    {
        NotifyUserActivity("webview");
    }
}

private void StartMediaTimer()
{
    StopMediaTimer();
    var ms = Math.Max(500, _config.SensorPollMs);
    _mediaTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(ms)
    };
    _mediaTimer.Tick += MediaTimer_Tick;
    _mediaTimer.Start();
    _log($"Media session timer started ({ms} ms) on UI thread.");
}

private void StopMediaTimer()
{
    if (_mediaTimer is null) return;
    _mediaTimer.Tick -= MediaTimer_Tick;
    _mediaTimer.Stop();
    _mediaTimer = null;
}

private async void MediaTimer_Tick(object? sender, EventArgs e)
{
    try
    {
        var media = await _mediaSession.ReadAsync();
        await DispatchMediaSessionAsync(media);

        // tighten interval while playing for better resync
        if (_mediaTimer is not null)
        {
            var playing = string.Equals(media.Status, "Playing", StringComparison.OrdinalIgnoreCase);
            _mediaTimer.Interval = TimeSpan.FromMilliseconds(playing ? 500 : Math.Max(1000, _config.SensorPollMs));
        }

        // system volume → panel
        try
        {
            var (level, muted) = SystemVolume.GetState();
            await DispatchSystemVolumeAsync(level, muted);
        }
        catch (Exception vex)
        {
            _log($"Volume read: {vex.Message}");
        }
    }
    catch (Exception ex)
    {
        _log($"Media session: {ex.Message}");
    }
}



private void StartInfoTimers()
{
    StopInfoTimers();

    _weatherTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMinutes(Math.Max(5, _config.WeatherPollMinutes))
    };
    _weatherTimer.Tick += async (_, _) => await PollWeatherAsync();
    _weatherTimer.Start();

    _rssTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMinutes(Math.Max(5, _config.RssPollMinutes))
    };
    _rssTimer.Tick += async (_, _) => await PollRssAsync();
    _rssTimer.Start();

    _ = PollWeatherAsync();
    _ = PollRssAsync();
}

private void StopInfoTimers()
{
    if (_weatherTimer is not null)
    {
        _weatherTimer.Stop();
        _weatherTimer = null;
    }
    if (_rssTimer is not null)
    {
        _rssTimer.Stop();
        _rssTimer = null;
    }
}

private async Task PollWeatherAsync()
{
    try
    {
        var snap = await _weather.ReadAsync();
        await DispatchWeatherAsync(snap);
    }
    catch (Exception ex)
    {
        _log($"Weather: {ex.Message}");
    }
}

private async Task PollRssAsync()
{
    try
    {
        var snap = await _rss.ReadAsync();
        await DispatchRssAsync(snap);
    }
    catch (Exception ex)
    {
        _log($"RSS: {ex.Message}");
    }
}

private Task DispatchWeatherAsync(WeatherSnapshot snap)
{
    if (_webView?.CoreWebView2 is null) return Task.CompletedTask;
    var json = JsonSerializer.Serialize(snap, JsonOptions);
    return OnUiAsync(() =>
        _webView!.CoreWebView2!.ExecuteScriptAsync(
            $"window.dispatchEvent(new CustomEvent('weather-snapshot', {{ detail: {json} }}));"));
}

private Task DispatchRssAsync(RssSnapshot snap)
{
    if (_webView?.CoreWebView2 is null) return Task.CompletedTask;
    var json = JsonSerializer.Serialize(snap, JsonOptions);
    return OnUiAsync(() =>
        _webView!.CoreWebView2!.ExecuteScriptAsync(
            $"window.dispatchEvent(new CustomEvent('rss-snapshot', {{ detail: {json} }}));"));
}

private Task DispatchSystemVolumeAsync(float level, bool muted)
{
    if (_webView?.CoreWebView2 is null) return Task.CompletedTask;

    var payload = new { level, muted };
    var json = JsonSerializer.Serialize(payload, JsonOptions);

    return OnUiAsync(() =>
        _webView!.CoreWebView2!.ExecuteScriptAsync(
            $"window.dispatchEvent(new CustomEvent('system-volume', {{ detail: {json} }}));"));
}

    private async Task SensorLoopAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(_config.SensorPollMs, 250));
        var storageInterval = Math.Max(_config.StorageHealthPollMs, 10000);
        long nowMs = 0;
        while (!token.IsCancellationRequested)
        {
            //Dispatch for AIDA64
            try
            {
                var snapshot = _aida64.ReadSnapshot();
                _lastSnapshot = snapshot;
                await DispatchSnapshotAsync(snapshot);
                await DispatchNetworkSnapshotAsync(_network.ReadSnapshot());

                nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (nowMs - _lastStorageDispatchUtcMs >= storageInterval)
                {
                    await DispatchStorageSnapshotAsync(_storage.ReadSnapshot());
                    _lastStorageDispatchUtcMs = nowMs;
                }
                PublishStatus();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _log($"AIDA64 loop error: {ex.Message}");
                PublishStatus();
            }
            await DelayQuietly(interval, token);
        }
    }

    private async Task PrimePanelDataAsync()
    {
        try
        {
            var snapshot = _aida64.ReadSnapshot();
            _lastSnapshot = snapshot;
            await DispatchSnapshotAsync(snapshot);
            await DispatchNetworkSnapshotAsync(_network.ReadSnapshot());
            await DispatchStorageSnapshotAsync(_storage.ReadSnapshot());
            _lastStorageDispatchUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _log($"Initial data prime failed: {ex.Message}");
        }
    }

    private async Task DispatchSnapshotAsync(SensorSnapshot snapshot)
    {
        if (_webView is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await OnUiAsync(() =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return Task.CompletedTask;
            }

            return _webView.ExecuteScriptAsync(
                $"window.dispatchEvent(new CustomEvent('aida64-sensors', {{ detail: {json} }}));");
        });
    }

    private async Task DispatchNetworkSnapshotAsync(NetworkSnapshot snapshot)
    {
        if (_webView is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await OnUiAsync(() =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return Task.CompletedTask;
            }

            return _webView.ExecuteScriptAsync(
                $"window.dispatchEvent(new CustomEvent('network-snapshot', {{ detail: {json} }}));");
        });
    }

    private async Task DispatchStorageSnapshotAsync(StorageSnapshot snapshot)
    {
        if (_webView is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await OnUiAsync(() =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return Task.CompletedTask;
            }

            return _webView.ExecuteScriptAsync(
                $"window.dispatchEvent(new CustomEvent('storage-snapshot', {{ detail: {json} }}));");
        });
    }

    private async Task FrameLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var frameStart = Stopwatch.StartNew();
            try
            {
                var yuyv = IsDeviceTest()
                    ? MakeDeviceTestFrame(_frames)
                    : await CaptureWebFrameAsync(token);

                if (_transport is not null)
                {
                    var sendSw = Stopwatch.StartNew();
                    if (!_transportStarted)
                    {
                        await _transport.StartAsync(_config, yuyv, token);
                        _transportStarted = true;
                        StartTouchInputIfNeeded(token);
                    }
                    else
                    {
                        await _transport.SendFrameAsync(yuyv, _frames, token);
                    }
                    sendSw.Stop();
                    _lastSendMs = sendSw.Elapsed.TotalMilliseconds;
                }

                _frames++;
                PublishStatus();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _log($"Frame loop error: {ex.Message}");
                PublishStatus();
                await DelayQuietly(TimeSpan.FromSeconds(1), token);
            }

            var fps = CurrentFps;
            var delay = TimeSpan.FromMilliseconds(1000.0 / fps) - frameStart.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await WaitForNextFrameOrActivityAsync(delay, token);
            }
        }
    }

    public void NotifyUserActivity(string source = "input")
    {
        var holdMs = Math.Max(_config.ActiveHoldMs, 250);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var until = now + holdMs;
        Interlocked.Exchange(ref _activeUntilUtcMs, until);
        SignalFrameLoop();
        if (now - Interlocked.Read(ref _lastActivityLogUtcMs) >= 1000)
        {
            Interlocked.Exchange(ref _lastActivityLogUtcMs, now);
            _log($"Activity: {source}; active refresh for {holdMs} ms.");
        }
    }

    public async Task SimulateClickAsync(double x, double y)
    {
        NotifyUserActivity("sim-click");
        await DispatchMouseEventAsync("mousePressed", x, y);
        await DispatchMouseEventAsync("mouseReleased", x, y);
    }

    private void StartTouchInputIfNeeded(CancellationToken token)
    {
        if (!_config.TouchEnabled || _touchInput is not null || _transport is null)
        {
            return;
        }

        _touchInput = new DashboardTouchInputService(_config, DispatchDashboardTouchAsync, _log);
        _touchInput.Start(token);
    }

    private async Task DispatchDashboardTouchAsync(DashboardTouchEvent touch)
    {
        NotifyUserActivity("touch");
        await DispatchMouseEventAsync(touch.Type, touch.X, touch.Y);
    }

    private Task DispatchMouseEventAsync(string type, double x, double y)
    {
        if (_webView is null)
        {
            return Task.CompletedTask;
        }

        return OnUiAsync(async () =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return;
            }

            var payload = JsonSerializer.Serialize(new
            {
                type,
                x,
                y,
                button = type == "mouseMoved" ? "none" : "left",
                buttons = type == "mouseReleased" ? 0 : 1,
                clickCount = type == "mouseMoved" ? 0 : 1
            });
            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", payload);
        });
    }

    private async Task<byte[]> CaptureWebFrameAsync(CancellationToken token)
    {
        if (_capture is null)
        {
            throw new InvalidOperationException("Frame capture service is not initialized.");
        }

        var (bgra, captureMs) = await OnUiAsync(() => _capture.CaptureBgraAsync());
        token.ThrowIfCancellationRequested();
        _lastCaptureMs = captureMs;

        var convertSw = Stopwatch.StartNew();
        var yuyv = YuyvConverter.BgraToYuyv(bgra, _config.Width, _config.Height);
        convertSw.Stop();
        _lastConvertMs = convertSw.Elapsed.TotalMilliseconds;
        return yuyv;
    }

    private byte[] MakeDeviceTestFrame(long frame)
    {
        var phase = (int)(frame % 6);
        return phase switch
        {
            0 => YuyvConverter.MakeSolid(_config.Width, _config.Height, 255, 0, 0),
            1 => YuyvConverter.MakeSolid(_config.Width, _config.Height, 0, 255, 0),
            2 => YuyvConverter.MakeSolid(_config.Width, _config.Height, 0, 0, 255),
            3 => YuyvConverter.MakeSolid(_config.Width, _config.Height, 255, 255, 255),
            4 => YuyvConverter.MakeSolid(_config.Width, _config.Height, 0, 0, 0),
            _ => YuyvConverter.MakeSolid(_config.Width, _config.Height, 255, 192, 0)
        };
    }

    private void PublishStatus()
    {
        var online = _lastSnapshot?.Online == true
            ? $"online ({_lastSnapshot.Sensors.Count})"
            : $"offline{(_lastSnapshot?.Error is { Length: > 0 } err ? $": {err}" : "")}";
        var seconds = Math.Max(_wallClock.Elapsed.TotalSeconds, 0.001);
        var wallFps = _frames / seconds;
        var transportState = _transport?.State ?? "disabled";
        var lastError = _lastError.Length > 0 ? _lastError : _transport?.LastError ?? "";
        var status = new RuntimeStatus(
            _config.Mode,
            online,
            transportState,
            $"{_frames} @ {wallFps:0.00} fps / target {CurrentFps:0.0} {RefreshState} / cap {_lastCaptureMs:0.0} ms / yuyv {_lastConvertMs:0.0} ms / send {_lastSendMs:0.0} ms",
            lastError);

        _dispatcher.InvokeAsync(() => _status(status));
    }

    private double IdleFps => Math.Clamp(_config.IdleFps > 0 ? _config.IdleFps : _config.Fps, 0.2, 30.0);

    private double ActiveFps => Math.Clamp(_config.ActiveFps > 0 ? _config.ActiveFps : Math.Max(IdleFps, _config.Fps), 0.2, 30.0);

    private bool IsActiveRefresh => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < Interlocked.Read(ref _activeUntilUtcMs);

    private double CurrentFps => IsActiveRefresh ? ActiveFps : IdleFps;

    private string RefreshState => IsActiveRefresh ? "active" : "idle";

    private bool IsPreviewOnly() => string.Equals(_config.Mode, "preview-only", StringComparison.OrdinalIgnoreCase);

    private bool IsDeviceTest() => string.Equals(_config.Mode, "device-test", StringComparison.OrdinalIgnoreCase);

    private Task OnUiAsync(Func<Task> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return action();
        }
        return _dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return action();
        }
        return _dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private async Task WaitForNextFrameOrActivityAsync(TimeSpan delay, CancellationToken token)
    {
        Task signal;
        lock (_frameSignalLock)
        {
            signal = _frameSignal.Task;
        }

        try
        {
            await Task.WhenAny(Task.Delay(delay, token), signal);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void SignalFrameLoop()
    {
        TaskCompletionSource signal;
        lock (_frameSignalLock)
        {
            signal = _frameSignal;
            _frameSignal = NewFrameSignal();
        }
        signal.TrySetResult();
    }

    private Task DetachWebMessageHandlerAsync()
    {
        if (_webView is null)
        {
            return Task.CompletedTask;
        }

        return OnUiAsync(() =>
        {
            if (_webView.CoreWebView2 is not null)
            {
                _webView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;
            }
            return Task.CompletedTask;
        });
    }

    private static TaskCompletionSource NewFrameSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static async Task DelayQuietly(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static async Task WhenAllQuietly(params Task?[] tasks)
    {
        foreach (var task in tasks.Where(task => task is not null).Cast<Task>())
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DetachWebMessageHandlerAsync();
        await StopAsync();
    }
}
