using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Parses a user-selected Python PDK, converts its selected cells and saves the resulting library.</summary>
public interface IPdkImportWorkflow
{
    Task<PdkParseResult> ParseFromFileAsync(string pyFilePath, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    PdkDraft ConvertToPdkDraft(PdkParseResult result);
    Task SaveToJsonAsync(PdkDraft draft, string outputPath);
}
