using CAP_Core.Components.Core;

namespace CAP_Core.Components.Creation;

/// <summary>Saved group templates available to library and placement workflows.</summary>
public interface IGroupLibrary
{
    IReadOnlyList<GroupTemplate> Templates { get; }
    IEnumerable<GroupTemplate> UserTemplates { get; }
    IEnumerable<GroupTemplate> PdkTemplates { get; }
    void LoadTemplates();
    GroupTemplate SaveTemplate(ComponentGroup group, string name, string? description = null, string source = "User");
    bool RemoveTemplate(GroupTemplate template);
    ComponentGroup InstantiateTemplate(GroupTemplate template, double x, double y);
}
