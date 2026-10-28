namespace CAP.Avalonia.Services.AgentBridge.Contracts;

/// <summary>Settings capability; presentation does not construct or own the server transport.</summary>
public interface IAgentBridgeControl
{
    event EventHandler? StatusChanged;
    bool IsRunning { get; }
    int Port { get; }
    string? LastError { get; }
    Task ApplySettingsAsync();
}
