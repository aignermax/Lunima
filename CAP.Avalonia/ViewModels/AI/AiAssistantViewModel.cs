using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.AI;

/// <summary>
/// ViewModel for the in-app AI Design Assistant chat panel.
/// Manages conversation history, API key configuration, and communication
/// with the <see cref="IAiService"/>. When <see cref="IAiGridService"/> is
/// available, enables tool-calling so the AI can manipulate the circuit grid.
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

    [ObservableProperty] private string _userInput = "";
    [ObservableProperty] private bool _isTyping;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isSettingsExpanded;
    [ObservableProperty] private string _statusText = "";

    /// <summary>
    /// Initializes the ViewModel, loads persisted API key, and shows a welcome message.
    /// </summary>
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
    /// Uses tool-calling when a grid service is available.
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
                var tools = BuildGridToolDefinitions();
                response = await _aiService.SendMessageWithToolsAsync(
                    text, history, tools,
                    executeToolAsync: ExecuteToolOnUiThreadAsync,
                    ct: _cancellationSource.Token);
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
    private void CancelRequest() => _cancellationSource?.Cancel();

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

    /// <summary>Opens the Anthropic API keys page in the default browser.</summary>
    [RelayCommand]
    private void OpenApiKeyPage()
    {
        try
        {
            var url = "https://console.anthropic.com/settings/keys";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            StatusText = "Could not open browser. Visit: console.anthropic.com/settings/keys";
        }
    }

    /// <summary>
    /// Executes a tool on the Avalonia UI thread to safely modify ObservableCollections.
    /// </summary>
    private Task<string> ExecuteToolOnUiThreadAsync(string toolName, string inputJson) =>
        Dispatcher.UIThread.InvokeAsync(async () => await DispatchToolAsync(toolName, inputJson));

    private async Task<string> DispatchToolAsync(string toolName, string inputJson)
    {
        if (_gridService == null) return "Grid service not available.";

        StatusText = $"Executing: {toolName}...";
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var input = doc.RootElement;

            return toolName switch
            {
                "get_grid_state" => _gridService.GetGridState(),
                "get_available_types" => JsonSerializer.Serialize(_gridService.GetAvailableComponentTypes()),
                "place_component" => await _gridService.PlaceComponentAsync(
                    GetString(input, "component_type"),
                    GetDouble(input, "x"),
                    GetDouble(input, "y"),
                    GetInt(input, "rotation", 0)),
                "create_connection" => await _gridService.CreateConnectionAsync(
                    GetString(input, "from_component"),
                    GetString(input, "to_component")),
                "run_simulation" => await _gridService.RunSimulationAsync(),
                "get_light_values" => _gridService.GetLightValues(),
                "clear_grid" => _gridService.ClearGrid(),
                "create_group" => _gridService.CreateGroup(
                    GetStringArray(input, "component_ids"),
                    GetString(input, "group_name")),
                "ungroup" => _gridService.UngroupComponent(
                    GetString(input, "group_id")),
                "save_as_prefab" => _gridService.SaveGroupAsPrefab(
                    GetString(input, "group_id"),
                    GetString(input, "prefab_name"),
                    GetString(input, "description")),
                "copy_component" => await _gridService.CopyComponentAsync(
                    GetString(input, "source_id"),
                    GetDouble(input, "x"),
                    GetDouble(input, "y"),
                    GetInt(input, "rotation", -1)),
                _ => $"Unknown tool: {toolName}"
            };
        }
        catch (Exception ex)
        {
            return $"Tool '{toolName}' failed: {ex.Message}";
        }
        finally
        {
            StatusText = "";
        }
    }

    private static string GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

    private static double GetDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetDouble() : 0.0;

    private static IReadOnlyList<string> GetStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arrayEl) || arrayEl.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return arrayEl.EnumerateArray()
            .Select(item => item.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static int GetInt(JsonElement el, string key, int defaultVal = 0) =>
        el.TryGetProperty(key, out var v) ? v.GetInt32() : defaultVal;

    private IReadOnlyList<(string Role, string Content)> BuildConversationHistory() =>
        Messages
            .TakeLast(MaxHistoryMessages)
            .Where(m => m.Role is AiChatRole.User or AiChatRole.Assistant)
            .Select(m => (m.IsUser ? "user" : "assistant", m.Content))
            .ToList();

    private static IReadOnlyList<AiToolDefinition> BuildGridToolDefinitions() => new[]
    {
        new AiToolDefinition
        {
            Name = "get_grid_state",
            Description = "Get current photonic circuit grid state: placed components, connections, and available component types.",
            InputSchema = new { type = "object", properties = new { } }
        },
        new AiToolDefinition
        {
            Name = "get_available_types",
            Description = "Get a full list of all available component types from loaded PDKs.",
            InputSchema = new { type = "object", properties = new { } }
        },
        new AiToolDefinition
        {
            Name = "place_component",
            Description = "Place a photonic component on the grid at an approximate position. The system finds the nearest valid placement automatically.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    component_type = new { type = "string", description = "Exact component type name from the PDK" },
                    x = new { type = "number", description = "Target X center position in micrometers (0–5000)" },
                    y = new { type = "number", description = "Target Y center position in micrometers (0–5000)" },
                    rotation = new { type = "integer", description = "Rotation in degrees: 0, 90, 180, or 270 (optional, default 0)" }
                },
                required = new[] { "component_type", "x", "y" }
            }
        },
        new AiToolDefinition
        {
            Name = "create_connection",
            Description = "Connect two placed components with a waveguide. Automatically selects compatible unconnected pins.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    from_component = new { type = "string", description = "ID of the source component (use id from get_grid_state)" },
                    to_component = new { type = "string", description = "ID of the destination component (use id from get_grid_state)" }
                },
                required = new[] { "from_component", "to_component" }
            }
        },
        new AiToolDefinition
        {
            Name = "run_simulation",
            Description = "Run the S-Matrix light propagation simulation for the current circuit.",
            InputSchema = new { type = "object", properties = new { } }
        },
        new AiToolDefinition
        {
            Name = "get_light_values",
            Description = "Get current light propagation values (loss in dB, path length in µm) for all waveguide connections.",
            InputSchema = new { type = "object", properties = new { } }
        },
        new AiToolDefinition
        {
            Name = "clear_grid",
            Description = "Remove all components and connections from the photonic circuit grid.",
            InputSchema = new { type = "object", properties = new { } }
        },
        new AiToolDefinition
        {
            Name = "create_group",
            Description = "Group multiple components together into a ComponentGroup. Useful for organizing circuits and creating reusable subcircuits.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    component_ids = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Array of component IDs to group together (minimum 2 components)"
                    },
                    group_name = new { type = "string", description = "Optional name for the group" }
                },
                required = new[] { "component_ids" }
            }
        },
        new AiToolDefinition
        {
            Name = "ungroup",
            Description = "Ungroup a ComponentGroup back into individual components.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    group_id = new { type = "string", description = "ID of the group to ungroup" }
                },
                required = new[] { "group_id" }
            }
        },
        new AiToolDefinition
        {
            Name = "save_as_prefab",
            Description = "Save a ComponentGroup as a reusable prefab/template in the component library. The prefab will appear in the library panel.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    group_id = new { type = "string", description = "ID of the group to save as prefab" },
                    prefab_name = new { type = "string", description = "Name for the prefab template" },
                    description = new { type = "string", description = "Optional description of the prefab" }
                },
                required = new[] { "group_id", "prefab_name" }
            }
        },
        new AiToolDefinition
        {
            Name = "copy_component",
            Description = "Duplicate a component or group to a new position. Preserves all internal structure, frozen paths, and settings. Much faster than manually recreating circuits — use this for arrays, meshes, and symmetric designs.",
            InputSchema = new
            {
                type = "object",
                properties = new
                {
                    source_id = new { type = "string", description = "ID of the component or group to copy (use id from get_grid_state)" },
                    x = new { type = "number", description = "Target X position for the copy in micrometers" },
                    y = new { type = "number", description = "Target Y position for the copy in micrometers" },
                    rotation = new { type = "integer", description = "Rotation in degrees (0, 90, 180, 270). Optional, omit to keep source rotation. Not applied to groups." }
                },
                required = new[] { "source_id", "x", "y" }
            }
        }
    };

    private void ShowWelcomeMessage()
    {
        var hasKey = _aiService.IsConfigured;
        var gridCapable = _gridService != null;

        var welcome = (hasKey, gridCapable) switch
        {
            (false, _) =>
                "Hello! I'm your AI Design Assistant. Configure your Claude API key in the settings below to get started.",
            (true, true) =>
                "Hello! I'm your AI Design Assistant with grid manipulation capabilities. " +
                "Ask me to build a photonic circuit and I'll place components and connections for you!",
            (true, false) =>
                "Hello! I'm your AI Design Assistant. Describe a photonic circuit and I'll help you design it."
        };

        Messages.Add(new AiChatMessage { Role = AiChatRole.Assistant, Content = welcome });
    }
}
