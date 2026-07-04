using NAudio.CoreAudioApi;

namespace RavenAI.Services.Voice;

/// <summary>
/// Lists the active microphone (audio capture) endpoints via WASAPI. The device IDs returned are
/// WASAPI endpoint IDs, which the Azure Speech SDK's <c>AudioConfig.FromMicrophoneInput</c> takes
/// directly — so the list can drive live recognition without any extra mapping.
/// </summary>
public static class MicrophoneEnumerator
{
    /// <summary>
    /// The active capture devices, with a "Default microphone" entry first. Enumeration failures
    /// are swallowed so the default entry alone is always available.
    /// </summary>
    public static IReadOnlyList<AudioInputDevice> List()
    {
        var devices = new List<AudioInputDevice> { AudioInputDevice.Default };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    devices.Add(new AudioInputDevice(device.ID, device.FriendlyName));
            }
        }
        catch
        {
            // Fall back to just the default entry if WASAPI enumeration throws.
        }
        return devices;
    }
}
