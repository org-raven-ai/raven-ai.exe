using System.Threading;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;

namespace RavenAI.Services.Voice;

/// <summary>
/// Bridges an NAudio WASAPI capture (a microphone via <see cref="WasapiCapture"/>, or system
/// playback via <see cref="WasapiLoopbackCapture"/>) into an Azure Speech
/// <see cref="PushAudioInputStream"/> as 16 kHz / 16-bit / mono PCM (the format recognition
/// expects). Capture formats are typically 44.1/48 kHz / 32-bit float / stereo, so a
/// <see cref="MediaFoundationResampler"/> does the sample-rate + bit-depth + channel conversion in
/// one step on a background pump thread.
/// <para>
/// Using our own capture for the microphone — instead of the SDK's built-in
/// <c>AudioConfig.FromMicrophoneInput</c> — avoids the SDK's slow native device initialization,
/// which otherwise delays the first transcript by several seconds after start.
/// </para>
/// </summary>
internal sealed class WasapiAudioPusher : IDisposable
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1); // 16 kHz, 16-bit, mono

    private readonly IWaveIn _capture;
    private BufferedWaveProvider? _buffer;
    private MediaFoundationResampler? _resampler;
    private PushAudioInputStream? _pushStream;
    private Thread? _pump;
    private volatile bool _running;

    /// <param name="capture">
    /// The WASAPI capture to drain. This class owns it and disposes it. Pass a
    /// <see cref="WasapiCapture"/> for a microphone or a <see cref="WasapiLoopbackCapture"/> for
    /// system audio.
    /// </param>
    public WasapiAudioPusher(IWaveIn capture) => _capture = capture;

    /// <summary>
    /// True once audio has actually arrived this run. WASAPI stops raising <c>DataAvailable</c>
    /// when nothing is captured, so this lets the caller distinguish "listening but silent" from
    /// "actively capturing".
    /// </summary>
    public bool AudioReceived { get; private set; }

    /// <summary>
    /// Builds the push stream and an <see cref="AudioConfig"/> over it. Call before
    /// <see cref="Start"/>. The returned config is handed to the <c>SpeechRecognizer</c>.
    /// </summary>
    public AudioConfig CreateAudioConfig()
    {
        // BufferedWaveProvider is internally locked; one writer (DataAvailable) + one reader
        // (the pump) is the supported usage, so no extra synchronization is needed.
        _buffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            ReadFully = false,                       // don't synthesize silence during quiet periods
            BufferDuration = TimeSpan.FromSeconds(5) // cap memory if the consumer ever lags
        };
        _resampler = new MediaFoundationResampler(_buffer, TargetFormat);

        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        _pushStream = AudioInputStream.CreatePushStream(format);
        return AudioConfig.FromStreamInput(_pushStream);
    }

    public void Start()
    {
        if (_pushStream is null)
            throw new InvalidOperationException("CreateAudioConfig must be called first.");

        _capture.DataAvailable += OnDataAvailable;
        _running = true;
        _pump = new Thread(Pump) { IsBackground = true, Name = "WasapiAudioPusher" };
        _pump.Start();
        _capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        AudioReceived = true;
        _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    /// <summary>
    /// Drains the producer, joins the pump thread, then closes the push stream (EOF) — in that
    /// order — so the Azure SDK's internal pump never blocks on a stream that will never produce.
    /// The recognizer must be stopped AFTER this returns.
    /// </summary>
    public void Stop()
    {
        _running = false;
        try { _capture.StopRecording(); } catch { /* best effort */ }
        _pump?.Join();
        try { _pushStream?.Close(); } catch { /* best effort */ }
    }

    private void Pump()
    {
        // ~100 ms of 16 kHz / 16-bit / mono PCM.
        var chunk = new byte[3200];
        while (_running)
        {
            int read = _resampler?.Read(chunk, 0, chunk.Length) ?? 0;
            if (read > 0)
                _pushStream?.Write(chunk, read);
            else
                Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        Stop();
        _resampler?.Dispose();
        _capture.Dispose();
        _resampler = null;
        _buffer = null;
        _pushStream = null;
        _pump = null;
        AudioReceived = false;
    }
}
