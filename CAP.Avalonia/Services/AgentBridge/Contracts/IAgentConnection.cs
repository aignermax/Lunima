namespace CAP.Avalonia.Services.AgentBridge.Contracts;

/// <summary>User-approved client registration and its discoverability check.</summary>
public interface IAgentConnection
{
    string ManualCommand { get; }
    Task<bool?> IsRegisteredAsync(CancellationToken cancellationToken = default);
    Task<ClaudeRegistrationResult> ConnectAsync(CancellationToken cancellationToken = default);
}
