using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RavenAI.Models;
using RavenAI.Services.Chat;
using RavenAI.Services.Logging;
using RavenAI.Services.Voice;

namespace RavenAI.ViewModels;

/// <summary>
/// Drives the chat surface: message list, streaming send, and voice push-to-talk.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IChatProvider _chatProvider;
    private readonly ISpeechToText _stt;
    private readonly Func<ITextToSpeech> _ttsFactory;
    private readonly NAudioCapture _capture;

    private CancellationTokenSource? _streamCts;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ChatViewModel(
        IChatProvider chatProvider,
        ISpeechToText stt,
        Func<ITextToSpeech> ttsFactory,
        NAudioCapture capture)
    {
        _chatProvider = chatProvider;
        _stt = stt;
        _ttsFactory = ttsFactory;
        _capture = capture;
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        string text = InputText.Trim();
        InputText = string.Empty;
        await SendMessageAsync(text, speakReply: false);
    }

    /// <summary>Sends user text, appends an assistant bubble, and streams the reply into it.</summary>
    private async Task SendMessageAsync(string userText, bool speakReply)
    {
        if (string.IsNullOrWhiteSpace(userText)) return;

        ErrorMessage = string.Empty;
        Messages.Add(new ChatMessage(ChatRole.User, userText));

        var assistant = new ChatMessage(ChatRole.Assistant, string.Empty);
        Messages.Add(assistant);

        IsBusy = true;
        SendCommand.NotifyCanExecuteChanged();
        _streamCts = new CancellationTokenSource();

        var sb = new System.Text.StringBuilder();
        try
        {
            // Snapshot the conversation excluding the empty assistant placeholder.
            var history = Messages.Take(Messages.Count - 1).ToList();

            await foreach (string delta in _chatProvider.StreamAsync(history, _streamCts.Token))
            {
                sb.Append(delta);
                // Update on the UI thread so the bubble grows token-by-token.
                assistant.Content = sb.ToString();
            }

            if (sb.Length == 0)
                assistant.Content = "(no response)";

            if (speakReply && sb.Length > 0)
                await SpeakAsync(sb.ToString());
        }
        catch (OperationCanceledException)
        {
            assistant.Content = sb.Length > 0 ? sb + " …(stopped)" : "(cancelled)";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            assistant.Content = sb.Length > 0 ? sb.ToString() : "(error)";
            Log.Error("Chat request failed", ex, "Chat");
        }
        finally
        {
            IsBusy = false;
            SendCommand.NotifyCanExecuteChanged();
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    private async Task SpeakAsync(string text)
    {
        try
        {
            var tts = _ttsFactory();
            await tts.SpeakAsync(text);
            (tts as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"TTS error: {ex.Message}";
            Log.Error("Text-to-speech failed", ex, "Voice");
        }
    }

    [RelayCommand]
    private void StopStreaming() => _streamCts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Push-to-talk toggle: first press starts recording; second press stops, transcribes,
    /// sends, and speaks the reply. Wired to a dedicated hotkey and a mic button.
    /// </summary>
    [RelayCommand]
    public async Task ToggleVoiceAsync()
    {
        if (IsBusy) return;

        if (!IsRecording)
        {
            try
            {
                _capture.Start();
                IsRecording = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Microphone error: {ex.Message}";
                Log.Error("Microphone capture failed to start", ex, "Voice");
            }
            return;
        }

        // Stop + process.
        IsRecording = false;
        byte[] wav = _capture.Stop();
        if (wav.Length == 0) return;

        IsBusy = true;
        try
        {
            string transcript = await _stt.TranscribeAsync(wav);
            IsBusy = false;
            if (!string.IsNullOrWhiteSpace(transcript))
                await SendMessageAsync(transcript.Trim(), speakReply: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Transcription error: {ex.Message}";
            Log.Error("Voice transcription failed", ex, "Voice");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
