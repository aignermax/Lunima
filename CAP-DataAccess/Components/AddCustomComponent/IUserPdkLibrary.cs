using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.AddCustomComponent;

/// <summary>Owned user-PDK files: discovery, creation, editing, forking and recoverable removal.</summary>
public interface IUserPdkLibrary
{
    string RootDirectory { get; }
    IReadOnlyList<string> GetPdkFilePaths(string? directory = null);
    IReadOnlyList<UserPdkInfo> ListCustomPdks();
    string ResolvePath(ProcessDefinition process);
    string ResolveNamedPath(string pdkName);
    bool NamedPdkExists(string pdkName);
    bool ComponentExists(ProcessDefinition process, string componentName);
    bool ComponentExistsInFile(string filePath, string componentName);
    bool IsInManagedRoot(string filePath);
    string ForkBundledPdk(string bundledFilePath, string pdkName);
    string SaveDraftAsFork(PdkDraft draft, string pdkName);
    string CreateNamedPdkWithProcess(string pdkName, ProcessDefinition process, string backend, string? routingCrossSection);
    string Save(ProcessDefinition process, PdkComponentDraft component, string backend, string? routingCrossSection);
    string SaveToNamedPdk(string pdkName, ProcessDefinition process, PdkComponentDraft component, string backend, string? routingCrossSection);
    KeyedComponentSaveResult SaveComponentKeyed(string pdkName, ProcessDefinition process, PdkComponentDraft component,
        Predicate<PdkComponentDraft> isSameComponent, string backend, string? routingCrossSection);
    string AppendToExistingPdk(string filePath, PdkComponentDraft component);
    bool ReplaceComponent(string filePath, PdkComponentDraft component, bool backupFirst = true);
    string? RemoveComponent(string filePath, string componentName, bool backupFirst = true);
    string MoveToTrash(string filePath);
    IPdkTrash CreateTrashService();
}
