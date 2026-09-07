using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CAP.Avalonia.Services.Localization;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>
/// Guards the help-flyout text budget (#1152): a (?) flyout explains with short sections
/// plus an animation, so no section may grow back into a wall of text. "Help text" is any
/// <c>{loc:Localize Key}</c> used inside a <c>*HelpFlyout.axaml</c> control or inside an
/// inline <c>HelpFlyoutButton.HelpContent</c> block. Budget per key: ≤3 sentences in every
/// locale, ≤45 words in English (CJK locales have no spaces; German compounds inflate
/// counts — the English source of truth carries the word budget). Sentence counting splits
/// on . ! ? 。 ！ ？, so abbreviations ("e.g.") count as sentence ends — reword instead.
/// </summary>
public class HelpTextBudgetTests
{
    private const int MaxSentencesPerSection = 3;
    private const int MaxWordsPerSectionEn = 45;

    private static readonly string[] LocaleCodes = { "en", "de", "es", "ja", "zh-Hans" };

    private static readonly Regex LocalizeUsage = new(
        @"\{loc:Localize\s+([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    private static readonly Regex InlineHelpBlock = new(
        @"<(?:[A-Za-z]+:)?HelpFlyoutButton\.HelpContent>(.*?)</(?:[A-Za-z]+:)?HelpFlyoutButton\.HelpContent>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex SentenceSplit = new(
        @"(?<=[.!?。！？])\s*", RegexOptions.Compiled);

    private static readonly Regex Word = new(
        @"[A-Za-z0-9µ'’_-]+", RegexOptions.Compiled);

    /// <summary>
    /// Keys of flyouts not yet migrated to the short-sections-plus-animation pattern —
    /// currently the NewComponentWindow inline help (prose fits, but it keeps inline code
    /// examples and has no animation; migration is tracked in a follow-up issue). Adding
    /// an entry is a deliberate, review-visible act; remove it once the flyout is migrated.
    /// </summary>
    private static readonly HashSet<string> ExemptNotYetMigrated = new(StringComparer.Ordinal)
    {
        "NewComponent.HelpTitle",
        "NewComponent.HelpBody",
        "NewComponent.GdsFactoryExample",
        "NewComponent.NazcaExample",
    };

    [Fact]
    public void HelpFlyoutSections_StayWithinTheTextBudget()
    {
        var usages = CollectHelpTextKeys();
        usages.ShouldNotBeEmpty("no help-flyout text found — has the *HelpFlyout.axaml naming convention changed?");

        var tables = LocaleCodes.ToDictionary(code => code, LocalizationResourceLoader.Load);
        var violations = new List<string>();

        foreach (var (file, key) in usages)
        {
            if (ExemptNotYetMigrated.Contains(key)) continue;
            foreach (var code in LocaleCodes)
            {
                if (!tables[code].TryGetValue(key, out var value)) continue; // key parity is LocalizationCompletenessTests' job
                var sentences = SentenceSplit.Split(value.Trim())
                    .Count(segment => segment.Any(char.IsLetterOrDigit));
                if (sentences > MaxSentencesPerSection)
                    violations.Add($"{code}: {key} ({file}) has {sentences} sentences (max {MaxSentencesPerSection})");
                if (code == "en" && Word.Matches(value).Count > MaxWordsPerSectionEn)
                    violations.Add($"en: {key} ({file}) has {Word.Matches(value).Count} words (max {MaxWordsPerSectionEn})");
            }
        }

        violations.ShouldBeEmpty(
            "\nHelp-flyout sections exceed the text budget (≤3 sentences, ≤45 words):\n\n" +
            string.Join("\n", violations.Select(v => $"  ✗ {v}")) +
            "\n\nShorten the section or let an animation carry the explanation (docs/HELP-ANIMATIONS.md).\n" +
            "If the flyout is genuinely not migrated yet, add its keys to ExemptNotYetMigrated\n" +
            "and file a follow-up issue — but expect review pushback.");
    }

    /// <summary>
    /// All localize keys used as help-flyout text: everything inside a *HelpFlyout.axaml
    /// control (the convention for new flyouts) plus keys inside inline
    /// HelpFlyoutButton.HelpContent blocks (the pre-#1152 convention).
    /// </summary>
    private static List<(string File, string Key)> CollectHelpTextKeys()
    {
        var repoRoot = FindRepoRoot();
        var viewsRoot = Path.Combine(repoRoot, "CAP.Avalonia");
        var usages = new List<(string, string)>();

        foreach (var file in Directory.GetFiles(viewsRoot, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;
            var name = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            if (name.Contains("HelpFlyout", StringComparison.Ordinal) && name != "HelpFlyoutButton.axaml")
            {
                usages.AddRange(LocalizeUsage.Matches(text).Select(m => (rel, m.Groups[1].Value)));
                continue;
            }

            foreach (Match block in InlineHelpBlock.Matches(text))
                usages.AddRange(LocalizeUsage.Matches(block.Groups[1].Value).Select(m => (rel, m.Groups[1].Value)));
        }

        return usages.Distinct().ToList();
    }

    private static bool IsBuildOutput(string path)
    {
        var sep = Path.DirectorySeparatorChar;
        return path.Contains($"{sep}obj{sep}") || path.Contains($"{sep}bin{sep}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
