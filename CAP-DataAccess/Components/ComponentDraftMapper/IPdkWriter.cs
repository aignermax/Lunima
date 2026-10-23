using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Persists a PDK draft while enforcing the destination's write policy.</summary>
public interface IPdkWriter
{
    void SaveToFile(PdkDraft pdk, string filePath);
}
