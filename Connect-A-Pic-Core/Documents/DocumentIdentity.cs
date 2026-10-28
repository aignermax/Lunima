namespace CAP_Core.Documents;

/// <summary>A non-owning bookmark for an open document, independent of revision and view navigation.</summary>
public readonly struct DocumentIdentity
{
    internal DocumentIdentity(Guid value) => Value = value;
    internal Guid Value { get; }
}
