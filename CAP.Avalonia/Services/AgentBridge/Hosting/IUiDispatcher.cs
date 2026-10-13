using Avalonia.Threading;

namespace CAP.Avalonia.Services.AgentBridge.Hosting;

/// <summary>
/// Runs work on the UI thread. The agent bridge receives requests on thread-pool threads,
/// but the design model (canvas view-models, command history) is single-threaded UI state.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread and completes with its result. An
    /// awaited continuation inside <paramref name="work"/> frees the UI thread meanwhile.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

/// <summary>Production <see cref="IUiDispatcher"/> backed by Avalonia's UI dispatcher.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc/>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work) =>
        Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.InvokeAsync(work);
}
