using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services;

/// <summary>Finds imported geometry that cannot yet support a trustworthy simulation.</summary>
public static class ImportedModelReadiness
{
    /// <summary>Returns unresolved component names, including nested groups.</summary>
    public static IReadOnlyList<string> FindUnresolved(IEnumerable<Component> components)
    {
        var result = new List<string>();
        foreach (var component in components)
        {
            if (component.SimulationModelStatus == "geometry-only" && component.PhysicalPins.Count > 0)
                result.Add(component.Name);
            if (component is ComponentGroup group)
                result.AddRange(FindUnresolved(group.ChildComponents));
        }
        return result;
    }
}
