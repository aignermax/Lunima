namespace CAP.Avalonia.Services.AgentBridge.Contracts;

/// <summary>Registration outcome without exposing CLI processes to presentation.</summary>
public sealed record ClaudeRegistrationResult(bool Success, bool CliFound, string Output);
