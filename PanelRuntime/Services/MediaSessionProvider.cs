using System.IO;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace PanelRuntime.Services;

public sealed class MediaSessionSnapshot
{
    public bool Online { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Status { get; set; } = "Stopped"; // Playing | Paused | Stopped
    public bool CanPlay { get; set; }
    public bool CanPause { get; set; }
    public bool CanNext { get; set; }
    public bool CanPrev { get; set; }

    // <summary>data:image/...;base64,... or empty</summary>
    public string ThumbnailDataUrl { get; set; } = "";
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
}

public sealed class MediaSessionProvider
{
    private readonly Dispatcher _dispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private string _lastThumbKey = "";
    private string _lastThumbDataUrl = "";

    public MediaSessionProvider(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task<MediaSessionSnapshot> ReadAsync()
    {
    // Prefer direct call when already on UI (DispatcherTimer path)
        if (_dispatcher.CheckAccess())
            return ReadCoreAsync();
        return RunOnUiAsync(ReadCoreAsync);
    }

    public Task<bool> CommandAsync(string command)
        => RunOnUiAsync(() => CommandCoreAsync(command));

    public Task EnsureAsync()
        => RunOnUiAsync(EnsureCoreAsync);

    private async Task EnsureCoreAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync()
            .AsTask()
            .ConfigureAwait(true); // MUST stay on UI dispatcher
    }

    private async Task<MediaSessionSnapshot> ReadCoreAsync()
    {
        await EnsureCoreAsync().ConfigureAwait(true);

        var session = _manager!.GetCurrentSession();
        if (session is null)
        {
            return new MediaSessionSnapshot { Online = false, Status = "Stopped" };
        }

        var props = await session.TryGetMediaPropertiesAsync()
            .AsTask()
            .ConfigureAwait(true);

        
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var controls = playback.Controls;

        var status = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "Playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "Paused",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "Stopped",
            _ => playback.PlaybackStatus.ToString()
        };

        var title = props?.Title?.Trim() ?? "";
        var artist = props?.Artist?.Trim() ?? "";
        var album = props?.AlbumTitle?.Trim() ?? "";
        var app = session.SourceAppUserModelId ?? "";
        // Cache thumbnail by title+artist so we don't re-encode every poll
        var thumbKey = $"{title}|{artist}|{album}";
        var thumb = _lastThumbDataUrl;
        if (thumbKey != _lastThumbKey)
        {
            thumb = await TryReadThumbnailAsync(props).ConfigureAwait(true);
            _lastThumbKey = thumbKey;
            _lastThumbDataUrl = thumb;
        }

        return new MediaSessionSnapshot
        {
            Online = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist),
            Title = string.IsNullOrWhiteSpace(title) ? "Unknown title" : title,
            Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist,
            Album = album,
            AppName = app,
            Status = status,
            CanPlay = controls.IsPlayEnabled,
            CanPause = controls.IsPauseEnabled,
            CanNext = controls.IsNextEnabled,
            CanPrev = controls.IsPreviousEnabled,
            ThumbnailDataUrl = thumb,
            PositionSeconds = timeline.Position.TotalSeconds,
            DurationSeconds = timeline.EndTime.TotalSeconds
        };
    }

    private async Task<bool> CommandCoreAsync(string command)
    {
        await EnsureCoreAsync().ConfigureAwait(true);
        var session = _manager!.GetCurrentSession();
        if (session is null) return false;

        var op = command.ToLowerInvariant() switch
        {
            "play" => session.TryPlayAsync(),
            "pause" => session.TryPauseAsync(),
            "playpause" => session.TryTogglePlayPauseAsync(),
            "next" => session.TrySkipNextAsync(),
            "prev" or "previous" => session.TrySkipPreviousAsync(),
            _ => null
        };
        if (op is null) return false;
        return await op.AsTask().ConfigureAwait(true);
    }

    private static async Task<string> TryReadThumbnailAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties? props)
    {
        try
        {
            if (props?.Thumbnail is null) return "";
            using var stream = await props.Thumbnail.OpenReadAsync()
                .AsTask()
                .ConfigureAwait(true);
            if (stream.Size == 0 || stream.Size > 6_000_000) return "";

            var bytes = new byte[stream.Size];
            await stream.AsStreamForRead().ReadExactlyAsync(bytes).ConfigureAwait(true);

            var mime = "image/jpeg";
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50) mime = "image/png";
            else if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49) mime = "image/gif";

            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Runs the whole async body on the WPF UI dispatcher and keeps
    /// continuations there (ConfigureAwait(true) inside the body).
    /// </summary>
    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return action();
        }

        // InvokeAsync(Func<Task<T>>) schedules on UI; Unwrap joins the inner task.
        return _dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private Task RunOnUiAsync(Func<Task> action)
    {
        if (_dispatcher.CheckAccess())
        {
            return action();
        }

        return _dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}