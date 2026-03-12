using System.Text.Json;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.Components.ComponentHelpers;

/// <summary>
/// Manages loading, saving, and instantiating component groups.
/// </summary>
public class ComponentGroupManager
{
    private readonly string _catalogPath;
    private readonly Dictionary<Guid, ComponentGroup> _groups = new();

    /// <summary>
    /// All loaded component groups.
    /// </summary>
    public IReadOnlyList<ComponentGroup> Groups => _groups.Values.ToList();

    public ComponentGroupManager(string catalogPath)
    {
        _catalogPath = catalogPath;
        LoadCatalog();
    }

    /// <summary>
    /// Creates a new component group from selected components on the canvas.
    /// </summary>
    public ComponentGroup CreateGroupFromComponents(
        string name,
        string category,
        IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections)
    {
        if (components.Count == 0)
            throw new ArgumentException("Cannot create group from zero components", nameof(components));

        // Calculate bounding box
        var minX = components.Min(c => c.PhysicalX);
        var minY = components.Min(c => c.PhysicalY);
        var maxX = components.Max(c => c.PhysicalX + c.WidthMicrometers);
        var maxY = components.Max(c => c.PhysicalY + c.HeightMicrometers);

        var group = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            WidthMicrometers = maxX - minX,
            HeightMicrometers = maxY - minY,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        // Build component member list with relative positions
        var componentMap = new Dictionary<Component, int>();
        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            componentMap[comp] = i;

            var member = new ComponentGroupMember
            {
                LocalId = i,
                TemplateName = comp.Identifier, // Store the component identifier for recreation
                RelativeX = comp.PhysicalX - minX,
                RelativeY = comp.PhysicalY - minY,
                Rotation = comp.Rotation90CounterClock
            };

            // Store slider values for parametric components
            var sliders = comp.GetAllSliders();
            for (int j = 0; j < sliders.Count; j++)
            {
                member.Parameters[$"Slider{j}"] = sliders[j].Value;
            }

            group.Components.Add(member);
        }

        // Build connection list
        foreach (var conn in connections)
        {
            var sourceComp = conn.StartPin.ParentComponent;
            var targetComp = conn.EndPin.ParentComponent;

            // Only include connections where both endpoints are in the group
            if (componentMap.ContainsKey(sourceComp) && componentMap.ContainsKey(targetComp))
            {
                group.Connections.Add(new GroupConnection
                {
                    SourceComponentId = componentMap[sourceComp],
                    SourcePinName = conn.StartPin.Name,
                    TargetComponentId = componentMap[targetComp],
                    TargetPinName = conn.EndPin.Name
                });
            }
        }

        return group;
    }

    /// <summary>
    /// Saves a component group to the catalog.
    /// </summary>
    public void SaveGroup(ComponentGroup group)
    {
        group.ModifiedAt = DateTime.UtcNow;
        _groups[group.Id] = group;
        SaveCatalog();
    }

    /// <summary>
    /// Deletes a component group from the catalog.
    /// </summary>
    public void DeleteGroup(Guid groupId)
    {
        _groups.Remove(groupId);
        SaveCatalog();
    }

    /// <summary>
    /// Loads the component group catalog from disk.
    /// </summary>
    private void LoadCatalog()
    {
        if (!File.Exists(_catalogPath))
            return;

        try
        {
            var json = File.ReadAllText(_catalogPath);
            var groups = JsonSerializer.Deserialize<List<ComponentGroup>>(json, GetJsonOptions());
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    _groups[group.Id] = group;
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't crash - start with empty catalog
            Console.WriteLine($"Error loading component group catalog: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the component group catalog to disk.
    /// </summary>
    private void SaveCatalog()
    {
        try
        {
            var directory = Path.GetDirectoryName(_catalogPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_groups.Values.ToList(), GetJsonOptions());
            File.WriteAllText(_catalogPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving component group catalog: {ex.Message}");
            throw;
        }
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
