using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.AI;

/// <summary>
/// ViewModel for the in-app AI Design Assistant chat panel.
/// Manages conversation history, API key configuration, and communication
/// with the <see cref="IAiService"/>. When an <see cref="IAiGridService"/> is
/// provided, uses Claude function calling to directly manipulate the circuit grid.
/// </summary>
public partial class AiAssistantViewModel : ObservableObject
{
    private readonly IAiService _aiService;
    private readonly UserPreferencesService _preferencesService;
    private readonly IAiGridService? _gridService;
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
    /// <param name="aiService">The AI backend service (Claude API).</param>
    /// <param name="preferencesService">User preferences for persisting the API key.</param>
    /// <param name="gridService">Optional grid manipulation service for tool calling.</param>
    public AiAssistantViewModel(
        IAiService aiService,
        UserPreferencesService preferencesService,
        IAiGridService? gridService = null)
    {
        _aiService = aiService;
        _preferencesService = preferencesService;
        _gridService = gridService;

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
    /// Uses tool calling when a grid service is available.
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
            string response;

            if (_gridService != null)
            {
                var componentTypes = _gridService.GetAvailableComponentTypes();
                response = await _aiService.SendMessageWithToolsAsync(
                    text, history, componentTypes, ExecuteToolAsync, _cancellationSource.Token);
            }
            else
            {
                response = await _aiService.SendMessageAsync(text, history, _cancellationSource.Token);
            }

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
    /// Executes a named tool call from Claude and returns the result.
    /// Dispatches to the appropriate <see cref="IAiGridService"/> method.
    /// </summary>
    private async Task<string> ExecuteToolAsync(string toolName, string toolInputJson)
    {
        if (_gridService == null) return "Grid service not available.";

        try
        {
            using var doc = JsonDocument.Parse(toolInputJson);
            var root = doc.RootElement;

            return toolName switch
            {
                "get_grid_state" => _gridService.GetGridState(),
                "place_component" => _gridService.PlaceComponent(
                    root.GetProperty("component_type").GetString()!,
                    root.GetProperty("x").GetDouble(),
                    root.GetProperty("y").GetDouble(),
                    root.TryGetProperty("rotation", out var rot) ? rot.GetInt32() : 0),
                "create_connection" => _gridService.CreateConnection(
                    root.GetProperty("from_component").GetString()!,
                    root.GetProperty("to_component").GetString()!),
                "clear_grid" => _gridService.ClearGrid(),
                "start_simulation" => await _gridService.StartSimulationAsync(),
                "stop_simulation" => _gridService.StopSimulation(),
                "get_light_values" => _gridService.GetLightValues(),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            return $"Error executing {toolName}: {ex.Message}";
        }
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
        var gridNote = _gridService != null
            ? " I can now place components and build circuits directly on the canvas!"
            : "";

        var welcome = hasKey
            ? $"Hello! I'm your AI Design Assistant.{gridNote} Describe a photonic circuit and I'll help you design it."
            : "Hello! I'm your AI Design Assistant. Configure your Claude API key in the settings below to get started.";

        Messages.Add(new AiChatMessage { Role = AiChatRole.Assistant, Content = welcome });
    }
}
