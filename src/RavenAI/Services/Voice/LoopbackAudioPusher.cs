using System.Threading;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;

namespace RavenAI.Services.Voice;

/// <summary>
/// Captures system playback via WASAPI loopback and feeds it into an Azure Speech
/// <see cref="PushAudioInputStream"/> as 16 kHz / 16-bit / mono PCM (the format recognition
/// expects). The loopback mix format is typically 48 kHz / 32-bit float / stereo, so a
/// <see cref="MediaFoundationResampler"/> does the sample-rate + bit-depth + channel conversion
/// in one step on a background pump thread.
/// </summary>
internal sealed class LoopbackAudioPusher : IDisposable
{
    private static readonly WaveFormat TargetFormat = new(16000, 16, 1); // 16 kHz, 16-bit, mono

    private WasapiLoopbackCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private MediaFoundationResampler? _resampler;
    private PushAudioInputStream? _pushStream;
    private Thread? _pump;
    private volatile bool _running;

    /// <summary>
    /// True once loopback audio has actually arrived this run. WASAPI stops raising
    /// <c>DataAvailable</c> when nothing is playing, so this lets the caller distinguish
    /// "listening but silent" from "actively capturing".
    /// </summary>
    public bool AudioReceived { get; private set; }

    /// <summary>
    /// Builds the push stream and an <see cref="AudioConfig"/> over it. Call before
    /// <see cref="Start"/>. The returned config is handed to the <c>SpeechRecognizer</c>.
    /// </summary>
    public AudioConfig CreateAudioConfig()
    {
        _capture = new WasapiLoopbackCapture();
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
        if (_capture is null || _pushStream is null)
            throw new InvalidOperationException("CreateAudioConfig must be called first.");

        _capture.DataAvailable += OnDataAvailable;
        _running = true;
        _pump = new Thread(Pump) { IsBackground = true, Name = "LoopbackAudioPusher" };
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
        try { _capture?.StopRecording(); } catch { /* best effort */ }
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
        _capture?.Dispose();
        _capture = null;
        _resampler = null;
        _buffer = null;
        _pushStream = null;
        _pump = null;
        AudioReceived = false;
    }
}
