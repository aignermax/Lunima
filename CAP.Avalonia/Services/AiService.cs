using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CAP.Avalonia.Services;

/// <summary>
/// Claude (Anthropic) API integration for the in-app AI Design Assistant.
/// Sends messages to Claude and returns natural-language responses.
/// </summary>
public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private string _apiKey = "";

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ModelId = "claude-3-5-sonnet-20241022";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 1024;

    private const string SystemPrompt =
        "You are an AI assistant integrated into Lunima (Connect-A-PIC-Pro), a photonic integrated circuit design tool. " +
        "You help users design photonic circuits using natural language. " +
        "You can explain photonic concepts, suggest design approaches, and guide users through creating circuits. " +
        "Be concise, technical, and practical. " +
        "Available component types include: MMI splitters/combiners, directional couplers, " +
        "grating couplers, ring resonators, Mach-Zehnder interferometers, phase shifters, and straight waveguides. " +
        "When describing circuit designs, be specific about component choices and topology. " +
        "Keep responses focused and under 300 words unless more detail is clearly needed.";

    /// <inheritdoc/>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Initializes the AI service with a shared HTTP client.
    /// </summary>
    public AiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey ?? "";
    }

    /// <inheritdoc/>
    public async Task<string> SendMessageAsync(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "Please configure your Claude API key in the AI Assistant settings below.";

        var messages = BuildMessageList(userMessage, history);

        var requestBody = new
        {
            model = ModelId,
            max_tokens = MaxTokens,
            system = SystemPrompt,
            messages
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = BuildHttpRequest(json);

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return $"API error ({(int)response.StatusCode}): Check your API key and try again.";

            return ParseResponseText(responseJson);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return $"Network error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Unexpected error: {ex.Message}";
        }
    }

    private static List<object> BuildMessageList(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history)
    {
        var messages = history
            .Select(h => (object)new { role = h.Role, content = h.Content })
            .ToList();
        messages.Add(new { role = "user", content = userMessage });
        return messages;
    }

    private HttpRequestMessage BuildHttpRequest(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        return request;
    }

    private static string ParseResponseText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();
            return text ?? "No response received.";
        }
        catch
        {
            return "Could not parse AI response.";
        }
    }
}
