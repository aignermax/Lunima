using System.Text.Json;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.Services.GroupTemplates;

/// <summary>Loads a saved library entry's group payload on its first placement selection.</summary>
internal static class SavedGroupTemplateLoader
{
    /// <returns>False for a missing or empty payload. Malformed files propagate their original error.</returns>
    public static bool LoadIfNeeded(GroupTemplate template)
    {
        if (template.TemplateGroup != null || string.IsNullOrEmpty(template.FilePath)) return true;
        if (File.Exists(template.FilePath))
        {
            var data = JsonSerializer.Deserialize<GroupLibraryFileData>(File.ReadAllText(template.FilePath));
            if (data != null && !string.IsNullOrWhiteSpace(data.GroupData))
                template.TemplateGroup = GroupTemplateSerializer.Deserialize(data.GroupData);
        }
        return template.TemplateGroup != null;
    }
}
