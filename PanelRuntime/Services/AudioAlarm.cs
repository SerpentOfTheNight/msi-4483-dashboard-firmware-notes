// Services/AlarmAudio.cs
using System.IO;

public static class AlarmAudio
{
    private static System.Media.SoundPlayer? _player;

    public static void Play(string? path)
    {
        Stop();
        var file = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, "assets", "alarm.wav");
        if (!File.Exists(file))
        {
            // fallback system sound
            System.Media.SystemSounds.Exclamation.Play();
            return;
        }
        _player = new System.Media.SoundPlayer(file);
        _player.PlayLooping(); // or Play() once
    }

    public static void Stop()
    {
        try { _player?.Stop(); } catch { }
        _player?.Dispose();
        _player = null;
    }
}