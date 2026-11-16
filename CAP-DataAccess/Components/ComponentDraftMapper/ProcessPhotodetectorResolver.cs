using CAP_Core.LightCalculation.Photodetection;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>Boundary from optional PDK process data to explicitly selected absolute receivers.</summary>
public static class ProcessPhotodetectorResolver
{
    /// <summary>Resolves one output's process detector and explicit compact-model responsivity override.</summary>
    public static PhotodetectorDescription Resolve(PdkDraft pdk, PdkComponentDraft? output = null)
    {
        ArgumentNullException.ThrowIfNull(pdk);
        return PhotodetectorResolver.Resolve(pdk.Process?.Photodetector, output?.CompactModelParameters);
    }
}
