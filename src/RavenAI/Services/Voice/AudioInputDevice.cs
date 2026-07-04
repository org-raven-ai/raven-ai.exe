namespace RavenAI.Services.Voice;

/// <summary>
/// A selectable microphone (audio capture endpoint). <see cref="Id"/> is the WASAPI endpoint ID
/// that the Azure Speech SDK's <c>AudioConfig.FromMicrophoneInput</c> accepts; a null Id means the
/// system default device.
/// </summary>
public sealed record AudioInputDevice(string? Id, string Name)
{
    /// <summary>The "let Windows pick" entry shown first in the device list.</summary>
    public static AudioInputDevice Default { get; } = new(null, "Default microphone");
}
