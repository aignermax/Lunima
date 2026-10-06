namespace CAP.Avalonia.Services;

/// <summary>
/// Picks the tile icon of a shipped example design from its name, so the Home screen
/// shows a scannable grid instead of a wall of text. First matching rule wins; the
/// rules go from the most specific family to the generic flask.
/// </summary>
public static class ExampleIcon
{
    private static readonly (string[] Keywords, string Icon)[] Rules =
    {
        (new[] { "Chiplet", "Chiplets" }, "🔗"),
        (new[] { "RAM", "Memory", "Register" }, "🧮"),
        (new[] { "Adder", "ALU", "Counter", "Unit", "PC" }, "🔢"),
        (new[] { "Logic", "NAND", "AND", "NOT", "XOR", "OR", "Latch", "MUX" }, "⚡"),
        (new[] { "Ring" }, "⭕"),
        (new[] { "Mach-Zehnder", "Interferometer", "MZI" }, "〰️"),
    };

    /// <summary>Name prefix the tiles drop: the ⚡ icon already says it.</summary>
    private const string LogicGatePrefix = "Logic Gate ";

    /// <summary>The tile caption: the name without the redundant "Logic Gate " family prefix.</summary>
    public static string ShortName(string name) =>
        name.StartsWith(LogicGatePrefix, StringComparison.OrdinalIgnoreCase) ? name[LogicGatePrefix.Length..] : name;

    /// <summary>The icon for an example named <paramref name="name"/>.</summary>
    public static string For(string name)
    {
        foreach (var (keywords, icon) in Rules)
        {
            if (keywords.Any(k => ContainsWord(name, k)))
                return icon;
        }
        return "🧪";
    }

    /// <summary>True when <paramref name="keyword"/> occurs in <paramref name="name"/> as a whole word.</summary>
    private static bool ContainsWord(string name, string keyword)
    {
        int index = name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool startOk = index == 0 || !char.IsLetter(name[index - 1]);
            int end = index + keyword.Length;
            bool endOk = end >= name.Length || !char.IsLetter(name[end]);
            if (startOk && endOk) return true;
            index = name.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
