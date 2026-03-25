using System.Text.RegularExpressions;

namespace CAP_Core.Components.Creation;

/// <summary>
/// Generates readable, incremental names for copied components.
/// Converts names like "MMI_1x2_a3f5b2c8-9d4e-4f3a-b1c2-8e7d6f5a4b3c" to "MMI_1x2_1", "MMI_1x2_2", etc.
/// </summary>
public class ComponentNameGenerator
{
    /// <summary>
    /// Generates a unique, readable name for a copied component.
    /// Uses incremental suffixes (_1, _2, etc.) instead of GUIDs.
    /// </summary>
    /// <param name="originalName">The name of the component being copied</param>
    /// <param name="existingNames">All existing component names on the canvas</param>
    /// <returns>A new unique name with an incremental suffix</returns>
    public static string GenerateCopyName(string originalName, IEnumerable<string> existingNames)
    {
        if (string.IsNullOrWhiteSpace(originalName))
        {
            return $"component_1";
        }

        var nameSet = new HashSet<string>(existingNames);

        // Extract base name by removing existing numeric suffix or GUID suffix
        var baseName = ExtractBaseName(originalName);

        // Find the next available number
        int nextNumber = FindNextAvailableNumber(baseName, nameSet);

        return $"{baseName}_{nextNumber}";
    }

    /// <summary>
    /// Extracts the base name by removing numeric or GUID suffixes.
    /// Examples:
    /// - "MMI_1x2" → "MMI_1x2"
    /// - "MMI_1x2_1" → "MMI_1x2"
    /// - "MMI_1x2_a3f5b2c8" → "MMI_1x2"
    /// - "group_12345" → "group_12345" (keeps numeric if it looks like part of the name)
    /// </summary>
    private static string ExtractBaseName(string name)
    {
        // First, try to remove GUID-like suffix (32 hex chars with optional hyphens)
        var guidPattern = @"_[0-9a-fA-F]{8,32}(-[0-9a-fA-F]{4,})*$";
        var withoutGuid = Regex.Replace(name, guidPattern, "");

        // Then try to remove simple numeric suffix like _1, _2, _copy_1, etc.
        // But only if it's clearly a copy suffix (preceded by underscore)
        var numericSuffixPattern = @"_(copy_)?\d+$";
        var baseName = Regex.Replace(withoutGuid, numericSuffixPattern, "");

        // If we removed everything, return the original name
        return string.IsNullOrWhiteSpace(baseName) ? name : baseName;
    }

    /// <summary>
    /// Finds the next available number for the given base name.
    /// If "MMI_1x2_1" and "MMI_1x2_2" exist, returns 3.
    /// If no numbered versions exist, returns 1.
    /// </summary>
    private static int FindNextAvailableNumber(string baseName, HashSet<string> existingNames)
    {
        int maxNumber = 0;

        // Check all existing names that start with the base name (case-insensitive)
        foreach (var name in existingNames)
        {
            if (name.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            {
                // Try to extract the numeric suffix (case-insensitive regex)
                var pattern = $@"^{Regex.Escape(baseName)}_(\d+)$";
                var match = Regex.Match(name, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                {
                    maxNumber = Math.Max(maxNumber, number);
                }
            }
        }

        return maxNumber + 1;
    }

    /// <summary>
    /// Generates a unique name for a component group.
    /// Uses incremental suffixes instead of GUIDs for better readability.
    /// </summary>
    /// <param name="groupName">The original group name (e.g., "MyGroup")</param>
    /// <param name="existingNames">All existing component names on the canvas</param>
    /// <returns>A unique group identifier like "group_MyGroup_1"</returns>
    public static string GenerateGroupName(string groupName, IEnumerable<string> existingNames)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return GenerateCopyName("group", existingNames);
        }

        // For groups, use "group_<GroupName>_N" pattern
        var baseName = $"group_{groupName}";
        var nameSet = new HashSet<string>(existingNames);
        int nextNumber = FindNextAvailableNumber(baseName, nameSet);

        return $"{baseName}_{nextNumber}";
    }
}
