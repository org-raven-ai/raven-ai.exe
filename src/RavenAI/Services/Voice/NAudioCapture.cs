using System.IO;
using NAudio.Wave;

namespace RavenAI.Services.Voice;

/// <summary>
/// Push-to-talk microphone capture using NAudio's WaveInEvent. Records 16 kHz mono 16-bit
/// PCM (the format transcription endpoints expect) and returns a complete in-memory WAV.
/// </summary>
public sealed class NAudioCapture : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private readonly WaveFormat _format = new(16000, 16, 1); // 16 kHz, 16-bit, mono

    public bool IsRecording { get; private set; }

    public void Start()
    {
        if (IsRecording) return;

        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(_buffer, _format);
        _waveIn = new WaveInEvent { WaveFormat = _format, BufferMilliseconds = 50 };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
        IsRecording = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
        => _writer?.Write(e.Buffer, 0, e.BytesRecorded);

    /// <summary>Stops recording and returns the captured audio as WAV bytes (empty if nothing captured).</summary>
    public byte[] Stop()
    {
        if (!IsRecording) return Array.Empty<byte>();
        IsRecording = false;

        _waveIn!.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.Dispose();
        _waveIn = null;

        _writer!.Flush();
        byte[] bytes = _buffer!.ToArray(); // grab WAV bytes before disposing the writer
        _writer.Dispose();                 // disposes the underlying MemoryStream too
        _writer = null;
        _buffer = null;
        return bytes;
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            try { Stop(); } catch { /* best effort */ }
        }
        _waveIn?.Dispose();
        _writer?.Dispose();
        _buffer?.Dispose();
    }
}
