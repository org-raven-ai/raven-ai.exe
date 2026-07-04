using CommunityToolkit.Mvvm.ComponentModel;

namespace RavenAi.Models;

public enum ChatRole { User, Assistant, System }

/// <summary>
/// One message in the conversation. Observable so the assistant bubble can grow
/// token-by-token during streaming.
/// </summary>
public sealed partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _content = string.Empty;

    public ChatRole Role { get; }

    public bool IsUser => Role == ChatRole.User;

    public ChatMessage(ChatRole role, string content = "")
    {
        Role = role;
        _content = content;
    }
}
