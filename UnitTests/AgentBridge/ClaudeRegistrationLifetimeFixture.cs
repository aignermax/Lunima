using System.Diagnostics;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP.Avalonia.Services.AgentBridge.Registration;
using ProcessLaunchFactory = CAP_Core.Export.ProcessLaunchFactory;

namespace UnitTests.AgentBridge;

internal sealed class ClaudeRegistrationLifetimeFixture : IDisposable
{
    private readonly string _preferencesPath = Path.Combine(Path.GetTempPath(), $"claude-lifetime-{Guid.NewGuid():N}.json");
    internal UserPreferencesService Preferences { get; }
    internal HeldHost Host { get; } = new();
    internal List<ProcessStartInfo> Calls { get; } = new();
    internal Func<ProcessStartInfo, CancellationToken, Task<CliRunResult>>? Run { get; set; }
    internal ClaudeCodeRegistrar Registrar { get; }
    internal ClaudeCodeConnector Connector { get; }

    internal ClaudeRegistrationLifetimeFixture()
    {
        Preferences = new UserPreferencesService(_preferencesPath);
        var directory = Path.Combine(Path.GetTempPath(), "claude-lifetime-locator");
        var executable = Path.Combine(directory, "claude");
        var locator = new ClaudeCliLocator(path => path == executable, () => directory,
            _ => directory, isWindows: false, isMacOs: false);
        Registrar = new ClaudeCodeRegistrar(ProcessLaunchFactory.CreateDefault(), locator, (info, _, token) =>
        {
            Calls.Add(info);
            return Run?.Invoke(info, token) ?? Task.FromResult(new CliRunResult(0, "complete"));
        });
        Connector = new ClaudeCodeConnector(Preferences, Host, Registrar);
    }

    public void Dispose()
    {
        Host.Release.TrySetResult();
        File.Delete(_preferencesPath);
    }

    internal sealed class HeldHost : IAgentBridgeControl
    {
        internal bool Hold { get; set; }
        internal int Calls { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsRunning => false;
        public int Port => 0;
        public string? LastError => null;
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task ApplySettingsAsync()
        {
            Calls++;
            Entered.TrySetResult();
            return Hold ? Release.Task : Task.CompletedTask;
        }
    }
}
