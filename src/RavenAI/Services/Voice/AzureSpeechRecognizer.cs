using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RavenAI.Services.Voice;

/// <summary>
/// Continuous, real-time speech-to-text via the Azure Cognitive Services Speech SDK. Unlike the
/// batch <see cref="ISpeechToText"/> (which transcribes a finished WAV clip), this streams audio
/// live and raises interim + final results as you speak. Both the microphone and system audio are
/// captured with NAudio and pushed through the SDK (see <see cref="WasapiAudioPusher"/>); the
/// microphone deliberately avoids the SDK's built-in <c>FromMicrophoneInput</c>, whose slow native
/// device init delays the first transcript by several seconds.
/// </summary>
public sealed class AzureSpeechRecognizer : IDisposable
{
    private readonly Func<(string apiKey, string endpoint, string language)> _configProvider;

    private SpeechRecognizer? _recognizer;
    private WasapiAudioPusher? _pusher;
    private AudioConfig? _audioConfig;
    private TaskCompletionSource<bool>? _stoppedTcs;

    public bool IsRunning { get; private set; }

    /// <summary>Interim hypothesis; updates as more audio arrives. Raised on a background thread.</summary>
    public event Action<string>? InterimResult;

    /// <summary>Final, committed phrase. Raised on a background thread.</summary>
    public event Action<string>? FinalResult;

    /// <summary>Error message (e.g. auth failure). Raised on a background thread.</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>Raised once continuous recognition has started.</summary>
    public event Action? Started;

    /// <summary>Raised once continuous recognition has fully stopped.</summary>
    public event Action? Stopped;

    public AzureSpeechRecognizer(Func<(string apiKey, string endpoint, string language)> configProvider)
        => _configProvider = configProvider;

    /// <param name="microphoneDeviceId">
    /// WASAPI endpoint ID of the microphone to listen to (see <see cref="MicrophoneEnumerator"/>).
    /// Null/empty uses the system default device. Ignored when <paramref name="source"/> is system audio.
    /// </param>
    public async Task StartAsync(AudioInputSource source, string? microphoneDeviceId = null)
    {
        if (IsRunning) return;

        var (apiKey, endpoint, language) = _configProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No Azure Speech key configured. Add it in Settings.");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("No Azure Speech endpoint/region configured. Add it in Settings.");

        string endpointTrimmed = endpoint.Trim();
        var speechConfig = endpointTrimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? SpeechConfig.FromEndpoint(new Uri(endpointTrimmed), apiKey)
            : SpeechConfig.FromSubscription(apiKey, endpointTrimmed);
        speechConfig.SpeechRecognitionLanguage =
            string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim();

        // Both sources are captured with NAudio and pushed through the SDK. Using our own capture
        // for the microphone (rather than the SDK's FromMicrophoneInput) sidesteps the SDK's slow
        // native device init, so recognition starts within a fraction of a second.
        IWaveIn capture = source == AudioInputSource.SystemAudio
            ? new WasapiLoopbackCapture()
            : CreateMicrophoneCapture(microphoneDeviceId);
        _pusher = new WasapiAudioPusher(capture);
        _audioConfig = _pusher.CreateAudioConfig();

        _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);
        _stoppedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _recognizer.Recognizing += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Result.Text))
                InterimResult?.Invoke(e.Result.Text);
        };
        _recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrEmpty(e.Result.Text))
                FinalResult?.Invoke(e.Result.Text);
        };
        _recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
            {
                ErrorOccurred?.Invoke(string.IsNullOrWhiteSpace(e.ErrorDetails)
                    ? "Recognition was canceled." : e.ErrorDetails);
            }
            _stoppedTcs?.TrySetResult(false);
        };
        _recognizer.SessionStopped += (_, _) => _stoppedTcs?.TrySetResult(true);

        await _recognizer.StartContinuousRecognitionAsync();
        _pusher.Start();
        IsRunning = true;
        Started?.Invoke();
    }

    // Builds a WASAPI capture for the chosen microphone; falls back to the system default device
    // when no specific endpoint is selected. Device IDs are WASAPI endpoint IDs (see
    // <see cref="MicrophoneEnumerator"/>), which MMDeviceEnumerator.GetDevice takes directly.
    private static WasapiCapture CreateMicrophoneCapture(string? microphoneDeviceId)
    {
        if (string.IsNullOrEmpty(microphoneDeviceId))
            return new WasapiCapture();

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(microphoneDeviceId);
        return new WasapiCapture(device);
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;

        // Producer first: the pusher drains, joins its pump thread, and closes the push stream
        // (EOF) BEFORE the recognizer is stopped — reversing this can hang the SDK's pump on a
        // stream that will never produce. Dispose() does Stop() then frees native resources.
        _pusher?.Dispose();
        _pusher = null;

        if (_recognizer is not null)
        {
            try { await _recognizer.StopContinuousRecognitionAsync(); } catch { /* best effort */ }
            if (_stoppedTcs is not null)
                await _stoppedTcs.Task;
        }

        _recognizer?.Dispose();
        _recognizer = null;
        _audioConfig?.Dispose();
        _audioConfig = null;
        _stoppedTcs = null;

        Stopped?.Invoke();
    }

    public void Dispose()
    {
        if (IsRunning)
        {
            try { StopAsync().GetAwaiter().GetResult(); } catch { /* best effort */ }
        }
        else
        {
            _recognizer?.Dispose();
            _audioConfig?.Dispose();
            _pusher?.Dispose();
        }
    }
}
