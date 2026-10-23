namespace CAP_Core.Documents;

/// <summary>Owns admission and revision-scoped publication for one open design.</summary>
public sealed class DocumentSession
{
    private readonly object _sync = new();
    private readonly HashSet<DocumentOperation> _active = new();
    private Guid _document = Guid.NewGuid();
    private long _revision;
    private long _scopeRevision;
    private Guid? _scope;
    private bool _retired;
    private Task? _retirement;
    private DocumentOperation? _commitOwner;

    /// <summary>Failure raised by a cancellation callback; never substitutes for draining work.</summary>
    public AggregateException? CancellationError { get; private set; }

    /// <summary>Remembers the configured document without owning work or delaying retirement.</summary>
    public DocumentIdentity CaptureIdentity()
    {
        lock (_sync)
        {
            if (_retired) throw new OperationCanceledException("The document session is retired.");
            if (_commitOwner != null) throw new InvalidOperationException("Cannot bind to a partially committed document.");
            return new DocumentIdentity(_document);
        }
    }

    /// <summary>Captures revision and scope without retaining work or delaying retirement.</summary>
    public DocumentRevisionStamp CaptureRevision()
    {
        lock (_sync)
        {
            if (_retired) throw new OperationCanceledException("The document session is retired.");
            if (_commitOwner != null) throw new InvalidOperationException("Cannot capture a partially committed document.");
            return new(_document, _revision, _scopeRevision);
        }
    }

    /// <summary>Whether both document content and visible scope still match the captured revision.</summary>
    public bool IsCurrentRevision(DocumentRevisionStamp stamp)
    {
        lock (_sync) return !_retired && _commitOwner == null && stamp.Document == _document
            && stamp.Revision == _revision && stamp.ScopeRevision == _scopeRevision;
    }

    /// <summary>Whether a configured document remains open, regardless of edits or navigation.</summary>
    public bool IsCurrentIdentity(DocumentIdentity identity)
    {
        lock (_sync) return !_retired && _commitOwner == null && identity.Value == _document;
    }

    /// <summary>Admits new work only for the document to which its feature was configured.</summary>
    public DocumentOperation CaptureOperation(DocumentIdentity identity, CancellationToken cancellationToken = default,
        bool scopeBound = true) => CaptureOperationCore(identity, cancellationToken, scopeBound);

    /// <summary>Captures publication permission. Document-wide saves may ignore view navigation.</summary>
    public DocumentOperation CaptureOperation(CancellationToken cancellationToken = default, bool scopeBound = true) =>
        CaptureOperationCore(null, cancellationToken, scopeBound);

    private DocumentOperation CaptureOperationCore(DocumentIdentity? identity, CancellationToken cancellationToken,
        bool scopeBound)
    {
        lock (_sync)
        {
            if (_retired) throw new OperationCanceledException("The document session is retired.");
            if (identity is { } expected && expected.Value != _document)
                throw new OperationCanceledException("The configured document has been replaced.");
            if (_commitOwner != null) throw new InvalidOperationException("Cannot capture a partially committed document.");
            cancellationToken.ThrowIfCancellationRequested();
            var operation = new DocumentOperation(this, _document, _revision,
                scopeBound ? _scopeRevision : null, cancellationToken);
            _active.Add(operation);
            return operation;
        }
    }

    /// <summary>Revokes results captured before a document mutation.</summary>
    public void Invalidate() => Change(() =>
    {
        _revision++;
        // Synchronous observers of this batch are part of its mutation. Another
        // thread cannot inherit the exemption: it waits for the outer commit lock.
        if (_commitOwner is { } owner && MatchesIdentity(owner)) owner.Revision = _revision;
    });

    /// <summary>Changes only the visible scope; leaving and returning cannot revive an old lease.</summary>
    public void ChangeScope(Guid? scope)
    {
        Change(() =>
        {
            if (_scope == scope) return;
            _scope = scope;
            _scopeRevision++;
        });
    }

    /// <summary>Starts a new document identity in the still-open editor.</summary>
    public void ReplaceDocument() => Change(() =>
    {
        _document = Guid.NewGuid();
        _revision++;
        _scope = null;
        _scopeRevision++;
    });

    private void Change(Action change)
    {
        DocumentOperation[] obsolete;
        lock (_sync)
        {
            if (_retired) return;
            change();
            if (_commitOwner != null) return; // The outer commit owns cancellation dispatch.
            obsolete = _active.Where(operation => !Matches(operation)).ToArray();
        }
        // Cancellation callbacks are untrusted work. They run outside the document lock.
        _ = CancelAsync(obsolete);
    }

    internal bool IsCurrent(DocumentOperation operation)
    {
        lock (_sync) return CanPublish(operation);
    }

    internal bool TryPublish(DocumentOperation operation, Action publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (_sync)
        {
            if (!CanPublish(operation)) return false;
            publish();
            return true;
        }
    }

    internal bool TryCommitAndAdvance(DocumentOperation operation, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        DocumentOperation[] obsolete = [];
        try
        {
            lock (_sync)
            {
                if (_commitOwner != null) throw new InvalidOperationException("Document commits cannot be nested.");
                if (!CanPublish(operation)) return false;
                _commitOwner = operation;
                operation.Revision = ++_revision; // Revoke peers before any observer can see the batch.
                var succeeded = false;
                try
                {
                    commit();
                    succeeded = true;
                    return true;
                }
                finally
                {
                    // A throwing action may have changed the model. Revoke its
                    // worker too; this boundary promises no automatic rollback.
                    if (!succeeded) _revision++;
                    _commitOwner = null;
                    obsolete = _active.Where(candidate => !CanPublish(candidate)).ToArray();
                }
            }
        }
        finally { _ = CancelAsync(obsolete); }
    }

    private bool CanPublish(DocumentOperation operation) =>
        _commitOwner == null && !_retired && !operation.IsDisposed && _active.Contains(operation) && !operation.IsCancellationRequested
        && Matches(operation);

    private bool Matches(DocumentOperation operation) =>
        operation.Revision == _revision && MatchesIdentity(operation);

    private bool MatchesIdentity(DocumentOperation operation) => operation.DocumentId == _document
        && (operation.ScopeRevision == null || operation.ScopeRevision == _scopeRevision);

    internal void Complete(DocumentOperation operation)
    {
        lock (_sync) _active.Remove(operation);
    }

    internal void RecordCancellationError(AggregateException error)
    {
        lock (_sync) CancellationError = error;
    }

    /// <summary>Seals publication synchronously, then waits for every accepted operation's cleanup.</summary>
    public Task RetireAsync()
    {
        DocumentOperation[] operations;
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_retirement != null) return _retirement;
            _retired = true;
            operations = _active.ToArray();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = completion.Task;
        }
        _ = DrainAsync(operations, completion);
        return completion.Task;
    }

    private Task CancelAsync(DocumentOperation[] operations) => Task.WhenAll(operations.Select(CancelOneAsync));

    private async Task CancelOneAsync(DocumentOperation operation)
    {
        try { await operation.CancelAsync().ConfigureAwait(false); }
        catch (AggregateException error) { lock (_sync) CancellationError = error; }
    }

    private async Task DrainAsync(DocumentOperation[] operations, TaskCompletionSource completion)
    {
        try
        {
            await Task.WhenAll(CancelAsync(operations), Task.WhenAll(operations.Select(op => op.Completion)))
                .ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
