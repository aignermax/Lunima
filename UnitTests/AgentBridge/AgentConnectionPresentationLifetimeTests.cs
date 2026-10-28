using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.Services.AgentBridge.Contracts;
using CAP.Avalonia.ViewModels.Settings.AgentBridge;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class AgentConnectionPresentationLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ClosedPresentation_CancelsAndDrainsRegistrationWithoutLateFeedback(bool prompt, bool applicationOwner)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunima-agent-page-{Guid.NewGuid():N}.json");
        var preferences = new UserPreferencesService(path);
        var connection = new HeldConnection();
        var host = new Host();
        var application = new ApplicationWorkCoordinator();
        var settings = prompt ? null : new AgentBridgeSettingsViewModel(preferences, host, connection, application);
        var startup = prompt ? new ClaudeCodeConnectPromptViewModel(preferences, connection, "test", application) : null;
        object owner = (object?)settings ?? startup!;
        Task Run() => settings != null ? settings.ConnectClaudeCodeCommand.ExecuteAsync(null) : startup!.ConnectCommand.ExecuteAsync(null);
        string Feedback() => settings != null ? settings.FeedbackText : startup!.ResultText;
        var before = Feedback();
        var running = Run();
        var stopping = Task.CompletedTask;
        try
        {
            await connection.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (applicationOwner) stopping = application.StopAsync();
            else
            {
                if (owner is IDisposable synchronous) synchronous.Dispose();
                stopping = owner is IAsyncDisposable asynchronous ? asynchronous.DisposeAsync().AsTask() : Task.CompletedTask;
            }
            await connection.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection.Token.IsCancellationRequested.ShouldBeTrue("closing reaches the actual registration request");
            stopping.IsCompleted.ShouldBeFalse("accepted CLI work still owns its cleanup");
            connection.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Feedback().ShouldBe(before, "a closed page cannot publish a late result");
            await Run();
            connection.Calls.ShouldBe(1, "retired presentation must not register again");
            host.Subscribers.ShouldBe(0);
        }
        finally
        {
            connection.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            if (owner is IDisposable disposable) disposable.Dispose();
            File.Delete(path);
        }
    }

    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("port")]
    [InlineData("token")]
    [InlineData("require-token")]
    public async Task SettingsChangedDuringRegistration_RejectsOldAttempt(string change)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunima-agent-setting-{Guid.NewGuid():N}.json");
        var preferences = new UserPreferencesService(path);
        preferences.SetAgentBridgeEnabled(true);
        var connection = new HeldConnection();
        await using var model = new AgentBridgeSettingsViewModel(preferences, new Host(), connection);
        var running = model.ConnectClaudeCodeCommand.ExecuteAsync(null);
        try
        {
            await connection.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (change == "disabled") model.IsEnabled = false;
            else if (change == "port") { model.Port += 1; await model.ApplyPortCommand.ExecuteAsync(null); }
            else if (change == "token") model.RegenerateTokenCommand.Execute(null);
            else model.RequireToken = true;
            var enabled = model.IsEnabled;
            var feedback = model.FeedbackText;
            connection.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            model.IsEnabled.ShouldBe(enabled, "the completed old attempt cannot override a newer toggle");
            preferences.GetAgentBridgeEnabled().ShouldBe(enabled);
            model.FeedbackText.ShouldBe(feedback, "feedback belongs to the current connection configuration");
            connection.Token.IsCancellationRequested.ShouldBeTrue("configuration changes revoke the original CLI attempt");
        }
        finally { connection.Release.TrySetResult(); await running; File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToggleObserver_RevokesRegistrationFeedbackBeforeItIsPublished(bool close)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lunima-agent-reentrant-{Guid.NewGuid():N}.json");
        var preferences = new UserPreferencesService(path);
        var connection = new HeldConnection();
        await using var model = new AgentBridgeSettingsViewModel(preferences, new Host(), connection);
        var running = model.ConnectClaudeCodeCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        string? expectedFeedback = null;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(model.IsEnabled) || !model.IsEnabled) return;
            if (close) stopping = model.StopAsync();
            else model.RequireToken = true;
            expectedFeedback = model.FeedbackText;
        };
        try
        {
            await connection.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            connection.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            expectedFeedback.ShouldNotBeNull("the toggle observer must participate in actual result publication");
            model.FeedbackText.ShouldBe(expectedFeedback, "the observer revoked this registration result");
        }
        finally { connection.Release.TrySetResult(); await running; await stopping; File.Delete(path); }
    }

    private sealed class HeldConnection : IAgentConnection
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public string ManualCommand => "manual";
        public Task<bool?> IsRegisteredAsync(CancellationToken cancellationToken = default) => Task.FromResult<bool?>(false);
        public async Task<ClaudeRegistrationResult> ConnectAsync(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            using var cancellation = cancellationToken.Register(() => Cancelled.TrySetResult());
            Calls++;
            Entered.TrySetResult();
            await Release.Task;
            return new(true, true, "late result");
        }
    }

    private sealed class Host : IAgentBridgeControl
    {
        private EventHandler? _changed;
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public event EventHandler? StatusChanged { add => _changed += value; remove => _changed -= value; }
        public bool IsRunning => false;
        public int Port => 0;
        public string? LastError => null;
        public Task ApplySettingsAsync() => Task.CompletedTask;
    }
}
