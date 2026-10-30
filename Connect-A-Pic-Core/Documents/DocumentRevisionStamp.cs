namespace CAP_Core.Documents;

/// <summary>A non-owning identity for one exact document revision and navigation scope.</summary>
public readonly record struct DocumentRevisionStamp(Guid Document, long Revision, long ScopeRevision);
