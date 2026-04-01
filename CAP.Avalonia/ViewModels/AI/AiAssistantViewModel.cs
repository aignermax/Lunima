using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.AI;

/// <summary>
/// ViewModel for the in-app AI Design Assistant chat panel.
/// Manages conversation history, API key configuration, and communication
/// with the <see cref="IAiService"/>.
/// </summary>
public partial class AiAssistantViewModel : ObservableObject
{
    private readonly IAiService _aiService;
    private readonly UserPreferencesService _preferencesService;
    private CancellationTokenSource? _cancellationSource;

    private const int MaxHistoryMessages = 20;

    /// <summary>Gets the ordered chat history displayed in the panel.</summary>
    public ObservableCollection<AiChatMessage> Messages { get; } = new();

    [ObservableProperty]
    private string _userInput = "";

    [ObservableProperty]
    private bool _isTyping;

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private bool _isSettingsExpanded;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// Initializes the ViewModel, loads persisted API key, and shows a welcome message.
    /// </summary>
    public AiAssistantViewModel(IAiService aiService, UserPreferencesService preferencesService)
    {
        _aiService = aiService;
        _preferencesService = preferencesService;

        var savedKey = _preferencesService.GetAiApiKey();
        if (!string.IsNullOrEmpty(savedKey))
        {
            _apiKey = savedKey;
            _aiService.SetApiKey(savedKey);
        }

        ShowWelcomeMessage();
    }

    /// <summary>
    /// Sends the current <see cref="UserInput"/> to the AI and appends the response.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessage()
    {
        var text = UserInput.Trim();
        if (string.IsNullOrEmpty(text)) return;

        UserInput = "";
        Messages.Add(new AiChatMessage { Role = AiChatRole.User, Content = text });

        IsTyping = true;
        SendMessageCommand.NotifyCanExecuteChanged();
        StatusText = "AI is thinking...";

        _cancellationSource = new CancellationTokenSource();

        try
        {
            var history = BuildConversationHistory();
            var response = await _aiService.SendMessageAsync(text, history, _cancellationSource.Token);

            Messages.Add(new AiChatMessage { Role = AiChatRole.Assistant, Content = response });
            StatusText = "";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Request cancelled.";
        }
        finally
        {
            IsTyping = false;
            SendMessageCommand.NotifyCanExecuteChanged();
            _cancellationSource?.Dispose();
            _cancellationSource = null;
        }
    }

    private bool CanSend() => !IsTyping;

    /// <summary>Cancels an in-flight AI request.</summary>
    [RelayCommand]
    private void CancelRequest()
    {
        _cancellationSource?.Cancel();
    }

    /// <summary>Clears chat history and resets the conversation.</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        Messages.Add(new AiChatMessage
        {
            Role = AiChatRole.Assistant,
            Content = "Chat history cleared. How can I help you design your photonic circuit?"
        });
        StatusText = "";
    }

    /// <summary>Persists the API key to preferences and applies it to the service.</summary>
    [RelayCommand]
    private void SaveApiKey()
    {
        var key = ApiKey.Trim();
        _aiService.SetApiKey(key);
        _preferencesService.SetAiApiKey(key);
        IsSettingsExpanded = false;
        StatusText = string.IsNullOrEmpty(key) ? "API key cleared." : "API key saved.";
    }

    /// <summary>
    /// Builds conversation history for the API call.
    /// Returns the last <see cref="MaxHistoryMessages"/> user/assistant turns.
    /// </summary>
    private IReadOnlyList<(string Role, string Content)> BuildConversationHistory()
    {
        return Messages
            .TakeLast(MaxHistoryMessages)
            .Where(m => m.Role is AiChatRole.User or AiChatRole.Assistant)
            .Select(m => (m.IsUser ? "user" : "assistant", m.Content))
            .ToList();
    }

    private void ShowWelcomeMessage()
    {
        var hasKey = _aiService.IsConfigured;
        var welcome = hasKey
            ? "Hello! I'm your AI Design Assistant. Describe a photonic circuit and I'll help you design it."
            : "Hello! I'm your AI Design Assistant. Configure your Claude API key in the settings below to get started.";

        Messages.Add(new AiChatMessage { Role = AiChatRole.Assistant, Content = welcome });
    }
}
