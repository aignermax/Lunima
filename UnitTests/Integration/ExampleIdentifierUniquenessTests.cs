using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Saved connections reference components by identifier, so a shipped example must never
/// carry two components with the same identifier in one scope — the top level, or the leaf
/// children of one group (gate groups reuse leaf names like "input_a" among each other, which
/// their group scope keeps apart). A collision rewires the design on load.
/// </summary>
public class ExampleIdentifierUniquenessTests
{
    /// <summary>Every shipped example file.</summary>
    public static TheoryData<string> ExampleFiles => ExampleRouteBakeTests.ExampleFiles;

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Example_HasUniqueComponentIdentifiersPerScope(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Duplicates(document["Components"]?.AsArray()).ShouldBeEmpty($"'{exampleFileName}': top-level identifiers repeat");
        foreach (var group in document["Groups"]?.AsArray() ?? new JsonArray())
        {
            var name = group!["GroupDto"]!["GroupName"]!.GetValue<string>();
            Duplicates(group["ChildComponents"]?.AsArray())
                .ShouldBeEmpty($"'{exampleFileName}': identifiers repeat inside group '{name}'");
        }
    }

    private static List<string> Duplicates(JsonArray? components) =>
        (components ?? new JsonArray())
            .Select(c => c!["Identifier"]!.GetValue<string>())
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
}
