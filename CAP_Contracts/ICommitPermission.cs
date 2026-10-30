namespace CAP_Contracts;

/// <summary>Revocable permission to publish a synchronous change from an admitted operation.</summary>
public interface ICommitPermission
{
    CancellationToken CancellationToken { get; }
    /// <summary>Runs the change atomically with respect to revocation, or throws before invoking it.</summary>
    void Commit(Action change);
}
