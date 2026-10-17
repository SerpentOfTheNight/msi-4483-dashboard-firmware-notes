using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PanelRuntime.Models;

namespace PanelRuntime.Services;

public sealed class DashboardTransport : IDisposable
{
    private const int Report1Size = 64;
    private const int Report2Size = 1024;
    private const int Report2Payload = 1023;
    private const byte ReportId1 = 1;
    private const byte ReportId2 = 2;
    private const byte Magic0 = 0x6B;
    private const byte Magic1 = 0x5A;
    private const byte CmdOsdCtl = 0xF1;
    private const byte CmdMem32 = 0xF5;
    private const byte CmdStreamCtl = 0xF7;
    private const byte CmdSetDisplayMode = 0x50;
    private const byte CmdBrightness = 0x18;//Displays brightness
    private const byte StreamTargetRaw = 5;

    private const uint StaticPresentModeAddr = 0x001251EC;
    private const uint DynamicPresentVarAddr = 0x030FF084;
    private const uint PresentLastAddr = 0x030FF088;
    private const uint FreezeFlagAddr = 0x030FF080;
    private const uint VpeSkipFlagAddr = 0x030FF094;
    private const uint VpeSkipCountAddr = 0x030FF098;
    private const uint VpeBreatheCountAddr = 0x030FF0AC;
    private const uint VpeSkipMagic = 0x56504553;

    private readonly Action<string> _log;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly Nuc126KeepAlive _nuc;
    private SafeFileHandle? _dashboard;
    private RuntimeConfig? _config;
    private uint[] _buffers = [];
    private TimeSpan _dashboardPollInterval = TimeSpan.FromSeconds(1);
    private DateTime _lastDashboardPollUtc = DateTime.MinValue;
    private int _frameBytes = 480 * 800 * 2;
    private bool _presentModeEnabled;
    private bool _recovering;
    private uint _lastPresentedBuffer;
    private CancellationTokenSource? _presentKeepAliveCts;
    private Task? _presentKeepAliveTask;
    private int _presentKeepAliveFailures;

    public string State { get; private set; } = "stopped";
    public int DashboardPollSent { get; private set; }
    public int DashboardPollFailed { get; private set; }
    public string LastError { get; private set; } = "";

    public DashboardTransport(Action<string> log)
    {
        _log = log;
        _nuc = new Nuc126KeepAlive(log);
    }

    public async Task StartAsync(RuntimeConfig config, byte[] firstFrame, CancellationToken token)
    {
        _config = config;
        _buffers = config.ParseBuffers();
        _dashboardPollInterval = TimeSpan.FromMilliseconds(Math.Max(config.DashboardPollMs, 250));
        _frameBytes = checked(config.Width * config.Height * 2);
        State = "waking";

        if (!_nuc.Open())
        {
            throw new InvalidOperationException("NUC126 device not found.");
        }

        _nuc.Wake();
        await WaitDashboardOnlineAsync(TimeSpan.FromSeconds(45), token);
        _nuc.Start(TimeSpan.FromMilliseconds(Math.Max(config.NucHeartbeatMs, 500)), token);

        OpenDashboard();
        await _ioLock.WaitAsync(token);
        try
        {
            InitializeDashboardLocked(firstFrame);
            LastError = "";
        }
        finally
        {
            _ioLock.Release();
        }

        State = "running";
        StartPresentKeepAlive(config, token);
        _log("Dashboard transport started.");
    }

    public async Task SendFrameAsync(byte[] yuyvFrame, long frameNumber, CancellationToken token)
    {
        if (_dashboard is null || _dashboard.IsInvalid)
        {
            return;
        }

        var buffer = _buffers[(int)(frameNumber % _buffers.Length)];
        var sw = Stopwatch.StartNew();
        Exception? sendError = null;
        var needsRecovery = false;
        await _ioLock.WaitAsync(token);
        try
        {
            StreamFrameLocked(yuyvFrame, buffer);
            PresentLocked(buffer);
            if (ShouldPollDashboard(frameNumber))
            {
                PollDashboardLocked();
                _lastDashboardPollUtc = DateTime.UtcNow;
            }
            if (!needsRecovery && DashboardPollFailed == 0)
            {
                LastError = "";
            }
            State = $"running buffer=0x{buffer:X8}";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = "recovery";
            _log($"Dashboard send failed: {ex.Message}");
            sendError = ex;
            needsRecovery = true;
        }
        finally
        {
            _ioLock.Release();
            sw.Stop();
        }

        if (needsRecovery)
        {
            await RecoverAsync(yuyvFrame, token);
            if (State == "offline" && sendError is not null)
            {
                throw sendError;
            }
        }
    }

    public async Task CleanupAsync()
    {
        await StopPresentKeepAliveAsync();
        await _ioLock.WaitAsync();
        try
        {
            if (_dashboard is not null && !_dashboard.IsInvalid)
            {
                CleanupDashboardLocked();
            }
        }
        finally
        {
            _ioLock.Release();
        }

        await _nuc.StopAsync();
        CloseDashboard();
        _presentModeEnabled = false;
        _lastPresentedBuffer = 0;
        State = "stopped";
    }

    private void CleanupDashboardLocked()
    {
        _log("Dashboard cleanup started.");
        Try(() => StopStreamLocked());

        if (_config?.ClearBuffersOnExit == true && _buffers.Length > 0)
        {
            var black = YuyvConverter.MakeSolid(_config.Width, _config.Height, 0, 0, 0);
            foreach (var buffer in _buffers)
            {
                Try(() => StreamFrameLocked(black, buffer));
                Thread.Sleep(10);
            }

            Try(() => PresentLocked(_buffers[0]));
            Thread.Sleep(30);
        }

        Try(() => WriteMem32(VpeSkipFlagAddr, 0));
        Try(() => WriteMem32(DynamicPresentVarAddr, 0));
        Try(() => WriteMem32(PresentLastAddr, 0));
        Try(() => WriteMem32(FreezeFlagAddr, 0));
        Try(() => WriteMem32(StaticPresentModeAddr, 0));
        Try(() => SendOsdControl(2));

        if (_config?.RestoreDisplayOnExit == true)
        {
            Try(RestoreDisplayLocked);
        }

        Try(() => StopStreamLocked());
        LastError = "";
        _log("Dashboard cleanup completed.");
    }

    public async Task RecoverAsync(byte[] firstFrame, CancellationToken token)
    {
        if (_config is null || _recovering)
        {
            return;
        }

        _recovering = true;
        State = "recovery";
        _log("Dashboard recovery started.");
        await _ioLock.WaitAsync(token);
        try
        {
            CloseDashboard();
            _presentModeEnabled = false;
            _lastPresentedBuffer = 0;
        }
        finally
        {
            _ioLock.Release();
        }

        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    _nuc.Wake();
                }
                catch (Exception ex)
                {
                    LastError = $"NUC wake failed during recovery: {ex.Message}";
                    _log(LastError);
                }

                if (NativeHid.DeviceOnline(NativeHid.VidMsi, NativeHid.PidDashboard))
                {
                    await _ioLock.WaitAsync(token);
                    try
                    {
                        OpenDashboard();
                        InitializeDashboardLocked(firstFrame);
                        DashboardPollFailed = 0;
                        LastError = "";
                        State = "running";
                        _log("Dashboard recovery completed.");
                    }
                    finally
                    {
                        _ioLock.Release();
                    }
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }

            LastError = "Dashboard recovery timed out after 45 seconds.";
            State = "offline";
            _log(LastError);
        }
        finally
        {
            _recovering = false;
        }
    }

    private static void Try(Action action)
    {
        try { action(); } catch { }
    }

    private async Task WaitDashboardOnlineAsync(TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (NativeHid.DeviceOnline(NativeHid.VidMsi, NativeHid.PidDashboard))
            {
                return;
            }
            await Task.Delay(250, token);
        }

        throw new TimeoutException("Dashboard did not enumerate within 45 seconds.");
    }

    private void OpenDashboard()
    {
        CloseDashboard();
        var path = NativeHid.FindDevicePath(NativeHid.VidMsi, NativeHid.PidDashboard)
            ?? throw new InvalidOperationException("Dashboard HID device not found.");
        _dashboard = NativeHid.OpenReadWrite(path);
        _log("Dashboard HID opened.");
    }

    private void CloseDashboard()
    {
        _dashboard?.Dispose();
        _dashboard = null;
    }

    private void StreamFrameLocked(byte[] frame, uint dst)
    {
        if (frame.Length != _frameBytes)
        {
            throw new ArgumentException($"Frame size {frame.Length} != {_frameBytes}.");
        }

        FastStreamStart(dst, frame.Length);
        var report = new byte[Report2Size];
        report[0] = ReportId2;
        for (var offset = 0; offset < frame.Length; offset += Report2Payload)
        {
            var chunk = Math.Min(Report2Payload, frame.Length - offset);
            Array.Clear(report, 1, Report2Payload);
            Buffer.BlockCopy(frame, offset, report, 1, chunk);
            NativeHid.WriteFileExact(Handle, report);
        }
    }

    private void PresentLocked(uint addr)
    {
        WriteMem32(DynamicPresentVarAddr, addr);
        if (!_presentModeEnabled)
        {
            WriteMem32(StaticPresentModeAddr, 1);
            _presentModeEnabled = true;
        }
        _lastPresentedBuffer = addr;
    }

    private void StartPresentKeepAlive(RuntimeConfig config, CancellationToken externalToken)
    {
        if (_presentKeepAliveTask is not null || config.PresentKeepAliveMs <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(config.PresentKeepAliveMs, 100));
        _presentKeepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _presentKeepAliveTask = Task.Run(() => PresentKeepAliveLoopAsync(interval, _presentKeepAliveCts.Token));
        _log($"Dashboard present keepalive started: every {interval.TotalMilliseconds:0} ms.");
    }

    private async Task StopPresentKeepAliveAsync()
    {
        var cts = _presentKeepAliveCts;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        if (_presentKeepAliveTask is not null)
        {
            try
            {
                await _presentKeepAliveTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
        }

        cts.Dispose();
        _presentKeepAliveCts = null;
        _presentKeepAliveTask = null;
        _presentKeepAliveFailures = 0;
    }

    private async Task PresentKeepAliveLoopAsync(TimeSpan interval, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
                await _ioLock.WaitAsync(token);
                try
                {
                    if (_recovering || _dashboard is null || _dashboard.IsInvalid || !_presentModeEnabled || _lastPresentedBuffer == 0)
                    {
                        continue;
                    }

                    PresentLocked(_lastPresentedBuffer);
                    _presentKeepAliveFailures = 0;
                }
                finally
                {
                    _ioLock.Release();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _presentKeepAliveFailures++;
                LastError = $"Dashboard present keepalive failed: {ex.Message}";
                if (_presentKeepAliveFailures <= 3 || _presentKeepAliveFailures % 10 == 0)
                {
                    _log(LastError);
                }
            }
        }
    }

    private void InitializeDashboardLocked(byte[] firstFrame)
    {
        _presentModeEnabled = false;
        WriteMem32(VpeSkipCountAddr, 0);
        WriteMem32(VpeBreatheCountAddr, 0);
        WriteMem32(VpeSkipFlagAddr, VpeSkipMagic);
        WriteMem32(DynamicPresentVarAddr, 0);
        WriteMem32(PresentLastAddr, 0);
        WriteMem32(FreezeFlagAddr, 0);
        WriteMem32(StaticPresentModeAddr, 0);
        StreamFrameLocked(firstFrame, _buffers[0]);
        PresentLocked(_buffers[0]);
        Thread.Sleep(30);
        SendOsdControl(1);
        PollDashboardLocked();
        _lastDashboardPollUtc = DateTime.UtcNow;
        // Apply configured backlight (MSI Center uses HID cmd 0x18)
        var brightness = _config?.Brightness ?? 100;
        //Thread.Sleep(50);  // put in a slight debounce if glitchs occure 
        Try(() => SetBrightnessLocked(brightness));
    }

    private void FastStreamStart(uint dst, int total)
    {
        var report = MakeReport1(CmdStreamCtl);
        report[4] = 1;
        report[5] = StreamTargetRaw;
        WriteU32(report, 8, (uint)total);
        WriteU32(report, 12, 0);
        WriteU32(report, 16, dst);
        SendReport1(report);
    }

    private void StopStreamLocked()
    {
        var report = MakeReport1(CmdStreamCtl);
        report[4] = 2;
        report[5] = StreamTargetRaw;
        SendReport1(report);
    }

    private bool PollDashboardLocked()
    {
        try
        {
            var report = MakeReport1(0x14);
            WriteU32(report, 5, 4);
            SendReport1(report);
            Thread.Sleep(50);

            var input = new byte[Report1Size];
            input[0] = ReportId1;
            var ok = NativeHid.TryGetInputReport(Handle, input);
            if (ok && TryParseDashboardResponse(input, out var cmd, out var dataLen) && cmd == 0x15 && dataLen >= 4)
            {
                DashboardPollSent++;
                DashboardPollFailed = 0;
                LastError = "";
                return true;
            }

            DashboardPollFailed++;
            LastError = ok ? "Dashboard poll returned unexpected response." : "Dashboard poll GetInputReport failed.";
        }
        catch (Exception ex)
        {
            DashboardPollFailed++;
            LastError = $"Dashboard poll failed: {ex.Message}";
        }
        if (DashboardPollFailed >= 3)
        {
            _log($"Dashboard poll failed {DashboardPollFailed} times: {LastError}");
        }
        return false;
    }

    private bool ShouldPollDashboard(long frameNumber)
    {
        if (_config is null)
        {
            return false;
        }

        var everyFrames = Math.Max(_config.DashboardPollEveryFrames, 1);
        if (frameNumber > 0 && frameNumber % everyFrames == 0)
        {
            return true;
        }

        return DateTime.UtcNow - _lastDashboardPollUtc >= _dashboardPollInterval;
    }

    private static bool TryParseDashboardResponse(byte[] input, out byte cmd, out uint dataLen)
    {
        cmd = 0;
        dataLen = 0;
        var offset = input[0] == Magic0 && input[1] == Magic1 ? 0 : 1;
        if (input.Length < offset + 8 || input[offset] != Magic0 || input[offset + 1] != Magic1)
        {
            return false;
        }

        cmd = input[offset + 2];
        dataLen = ReadU32(input, offset + 4);
        return true;
    }

    private void WriteMem32(uint addr, uint value)
    {
        var report = MakeReport1(CmdMem32);
        report[4] = 1;
        WriteU32(report, 8, addr);
        WriteU32(report, 12, value);
        SendReport1(report);
    }

    private void SendOsdControl(byte subCommand)
    {
        var report = MakeReport1(CmdOsdCtl);
        report[4] = subCommand;
        SendReport1(report);
    }

    private void SetBrightnessLocked(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var report = MakeReport1(0x18); // report[0]=1, [1]=0x6B, [2]=0x5A, [3]=cmd
        // 01 6B 5A 18 00 | 04 00 00 00 | XX 00 00 00 ...
        report[4] = 0x00;
        report[5] = 0x04;
        report[6] = 0x00;
        report[7] = 0x00;
        report[8] = 0x00;
        report[9] = (byte)percent;  // 0..100
        report[10] = 0x00;
        report[11] = 0x00;
        SendReport1(report);
        _log($"Brightness set to {percent}%.");
    }

    private void RestoreDisplayLocked()
    {
        var mode = _config?.RestoreDisplayMode ?? "image";
        if (string.Equals(mode, "mainmenu", StringComparison.OrdinalIgnoreCase))
        {
            SetDisplayMode(8, 0, 1, [1]);
            _log("Display restored: MainMenu.");
            return;
        }

        var index = Math.Clamp(_config?.RestoreImageIndex ?? 7, 0, 15);
        var screens = new byte[index + 1];
        screens[index] = 1;
        SetDisplayMode(1, 0, screens.Length, screens);
        _log($"Display restored: Image({index}).");
    }

    private void SetDisplayMode(byte mode, byte slideShow, int count, ReadOnlySpan<byte> screens)
    {
        var report = MakeReport1(CmdSetDisplayMode);
        var payload = new byte[19];
        payload[0] = mode;
        payload[1] = slideShow;
        payload[2] = (byte)Math.Clamp(count, 0, 16);
        screens[..Math.Min(screens.Length, 16)].CopyTo(payload.AsSpan(3));
        WriteU32(report, 5, (uint)payload.Length);
        Buffer.BlockCopy(payload, 0, report, 9, payload.Length);
        SendReport1(report);
    }

    private static byte[] MakeReport1(byte cmd)
    {
        var report = new byte[Report1Size];
        report[0] = ReportId1;
        report[1] = Magic0;
        report[2] = Magic1;
        report[3] = cmd;
        return report;
    }

    private void SendReport1(byte[] report)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (NativeHid.TrySetOutputReport(Handle, report))
            {
                return;
            }

            Thread.Sleep(attempt == 0 ? 2 : 10);
        }

        throw new InvalidOperationException($"HidD_SetOutputReport failed for cmd 0x{report[3]:X2}.");
    }

    private SafeFileHandle Handle => _dashboard is { IsInvalid: false } handle
        ? handle
        : throw new InvalidOperationException("Dashboard HID is not open.");

    private static void WriteU32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static uint ReadU32(byte[] buffer, int offset)
    {
        return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
    }

    public void Dispose()
    {
        try { CleanupAsync().GetAwaiter().GetResult(); } catch { }
        _nuc.Dispose();
        _ioLock.Dispose();
    }
}
