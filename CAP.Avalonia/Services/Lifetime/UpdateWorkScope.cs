using CAP_Core.Update;

namespace CAP.Avalonia.Services.Lifetime;

/// <summary>Owns update requests and staged artifacts, without waiting on the command that requests shutdown.</summary>
internal sealed class UpdateWorkScope
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Task> _requests = new();
    private readonly HashSet<IUpdateDownload> _downloads = new();
    private bool _retired;
    private Task? _retirement;

    public bool IsRetired { get { lock (_gate) return _retired; } }

    public Task<GitHubReleaseInfo?> CheckAsync(Func<CancellationToken, Task<GitHubReleaseInfo?>> request,
        CancellationToken token = default) => Start(request, token);

    public Task<IUpdateDownload> PrepareAsync(Func<CancellationToken, Task<IUpdateDownload>> request) =>
        Start(request, CancellationToken.None);

    private Task<T> Start<T>(Func<CancellationToken, Task<T>> request, CancellationToken caller)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource linked;
        lock (_gate)
        {
            if (_retired) return Task.FromCanceled<T>(new CancellationToken(canceled: true));
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _stop.Token);
            _requests.Add(completion.Task);
        }
        _ = ExecuteAsync();
        return completion.Task;

        async Task ExecuteAsync()
        {
            T result = default!;
            Exception? failure = null;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                result = await request(linked.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (result is IUpdateDownload download)
                    {
                        if (_retired || linked.IsCancellationRequested) download.Dispose();
                        else _downloads.Add(download);
                    }
                    if (_retired || linked.IsCancellationRequested)
                        throw new OperationCanceledException(linked.Token);
                }
            }
            catch (Exception error) { failure = error; }
            finally
            {
                linked.Dispose();
                lock (_gate) _requests.Remove(completion.Task);
            }
            if (failure is OperationCanceledException) completion.TrySetCanceled();
            else if (failure != null) completion.TrySetException(failure);
            else completion.TrySetResult(result);
        }
    }

    public void Release(IUpdateDownload download)
    {
        lock (_gate) _downloads.Remove(download);
        download.Dispose();
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_retirement != null) return _retirement;
            _retired = true;
            return _retirement = DrainAsync(_requests.ToArray());
        }
    }

    private async Task DrainAsync(Task[] requests)
    {
        await ObserveAsync(_stop.CancelAsync()).ConfigureAwait(false);
        await ObserveAsync(Task.WhenAll(requests)).ConfigureAwait(false);
        IUpdateDownload[] downloads;
        lock (_gate) { downloads = _downloads.ToArray(); _downloads.Clear(); }
        foreach (var download in downloads) download.Dispose();
        _stop.Dispose();
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception) { /* Request callers own errors; retirement still drains and releases their artifacts. */ }
    }
}
