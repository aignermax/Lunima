using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Loads PDK drafts with strict, editing, or startup-tolerant validation.</summary>
public interface IPdkReader
{
    PdkDraft LoadFromFile(string filePath);
    PdkDraft LoadFromFileForEditing(string filePath);
    PdkDraft LoadFromJson(string json);
    List<PdkDraft> LoadFromDirectory(string directoryPath);
    PdkTolerantLoadResult LoadFromFileTolerant(string filePath);
}
