namespace CAP_DataAccess.Components.AddCustomComponent;

/// <summary>Discovery and restoration of recoverable user-PDK files and component backups.</summary>
public interface IPdkTrash
{
    string TrashDirectory { get; }
    IReadOnlyList<PdkTrashEntry> ListEntries();
    PdkTrashRestoreResult Restore(PdkTrashEntry entry);
    void PurgeExpired();
}
