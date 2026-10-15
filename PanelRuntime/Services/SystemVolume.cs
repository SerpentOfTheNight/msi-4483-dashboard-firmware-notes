using NAudio.CoreAudioApi;

namespace PanelRuntime.Services;

public static class SystemVolume
{
    private const float Step = 0.02f; // 5% per click
    private static float _levelBeforeMute = 0.5f;

    public static void Apply(string command)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var ep = device.AudioEndpointVolume;

        switch (command.Trim().ToLowerInvariant())
        {
            case "up":
                ep.MasterVolumeLevelScalar =
                    Math.Clamp(ep.MasterVolumeLevelScalar + Step, 0f, 1f);
                if (ep.Mute) ep.Mute = false;
                break;

            case "down":
                ep.MasterVolumeLevelScalar =
                    Math.Clamp(ep.MasterVolumeLevelScalar - Step, 0f, 1f);
                break;

            case "mute":
                // Prefer hardware mute; if unmute, restore last level
                if (!ep.Mute)
                {
                    _levelBeforeMute = ep.MasterVolumeLevelScalar;
                    if (_levelBeforeMute < 0.01f) _levelBeforeMute = 0.5f;
                    ep.Mute = true;
                    // Fallback some devices ignore Mute:
                    if (!ep.Mute)
                        ep.MasterVolumeLevelScalar = 0f;
                }
                else
                {
                    ep.Mute = false;
                    if (ep.MasterVolumeLevelScalar < 0.01f)
                        ep.MasterVolumeLevelScalar = Math.Clamp(_levelBeforeMute, Step, 1f);
                }




                
                break;
        }
    }

    public static (float Level, bool Muted) GetState()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var ep = device.AudioEndpointVolume;
        return (ep.MasterVolumeLevelScalar, ep.Mute);
    }
}