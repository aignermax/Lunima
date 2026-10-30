using System.Globalization;

namespace CAP.Avalonia.Services.AgentBridge.Contracts;

/// <summary>Shared connection syntax for the endpoint, registration and copyable UI text.</summary>
public static class AgentBridgeAddress
{
    public const string EndpointPath = "/mcp";
    public const string ServerName = "lunima";
    public static string EndpointUrl(int port) =>
        string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{EndpointPath}");

    public static IReadOnlyList<string> AddArguments(int port, string? token)
    {
        var arguments = new List<string>
        {
            "mcp", "add", "--scope", "user", "--transport", "http", ServerName, EndpointUrl(port)
        };
        if (!string.IsNullOrEmpty(token)) arguments.AddRange(["--header", $"Authorization: Bearer {token}"]);
        return arguments;
    }

    public static string ConnectCommand(int port, string? token) =>
        "claude " + string.Join(" ", AddArguments(port, token).Select(QuoteIfNeeded));
    private static string QuoteIfNeeded(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
