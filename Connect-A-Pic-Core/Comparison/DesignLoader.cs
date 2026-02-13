using System.Text.Json;

namespace CAP_Core.Comparison;

/// <summary>
/// Loads .cappro design files into read-only <see cref="DesignSnapshot"/> instances.
/// </summary>
public static class DesignLoader
{
    /// <summary>
    /// Loads a design snapshot from a .cappro JSON file.
    /// </summary>
    /// <param name="filePath">Absolute path to the .cappro file.</param>
    /// <returns>A read-only snapshot of the design.</returns>
    /// <exception cref="InvalidOperationException">If the file cannot be parsed.</exception>
    public static async Task<DesignSnapshot> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        return ParseJson(json, Path.GetFileNameWithoutExtension(filePath));
    }

    /// <summary>
    /// Parses a .cappro JSON string into a <see cref="DesignSnapshot"/>.
    /// </summary>
    public static DesignSnapshot ParseJson(string json, string name)
    {
        var fileData = JsonSerializer.Deserialize<DesignFileDto>(json);
        if (fileData == null)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize design file '{name}'.");
        }

        var components = fileData.Components
            .Select(c => new SnapshotComponent(
                c.TemplateName, c.X, c.Y, c.Identifier, c.Rotation))
            .ToList();

        var connections = fileData.Connections
            .Select(c => new SnapshotConnection(
                c.StartComponentIndex, c.StartPinName,
                c.EndComponentIndex, c.EndPinName))
            .ToList();

        return new DesignSnapshot(name, components, connections);
    }

    /// <summary>
    /// DTO matching the .cappro JSON file structure.
    /// Kept private — consumers use <see cref="DesignSnapshot"/>.
    /// </summary>
    private class DesignFileDto
    {
        public List<ComponentDto> Components { get; set; } = new();
        public List<ConnectionDto> Connections { get; set; } = new();
    }

    private class ComponentDto
    {
        public string TemplateName { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string Identifier { get; set; } = "";
        public int Rotation { get; set; }
    }

    private class ConnectionDto
    {
        public int StartComponentIndex { get; set; }
        public string StartPinName { get; set; } = "";
        public int EndComponentIndex { get; set; }
        public string EndPinName { get; set; } = "";
    }
}
