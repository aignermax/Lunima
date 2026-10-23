namespace CAP_DataAccess.Persistence.PIR;

/// <summary>User-global template model overrides, shared across open project workflows.</summary>
public interface ITemplateSMatrixStore
{
    /// <summary>Stable writable dictionary; call <see cref="Save"/> after mutations.</summary>
    Dictionary<string, ComponentSMatrixData> Overrides { get; }
    void Save();
}
