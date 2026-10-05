using System.Text.RegularExpressions;
using CAP.Avalonia.Services.Localization;
using Shouldly;
using Xunit;

namespace UnitTests.Services.Localization;

/// <summary>
/// Sentence-count budget for the ONA help flyout (#1432): each section is meant to be
/// scannable in a glance, so the two body strings stay at or below three sentences in
/// the English source of truth. A stricter count here keeps future edits from quietly
/// growing the flyout into a wall of text.
/// </summary>
public partial class HelpTextBudgetTests
{
    private const int MaxSentencesPerHelpBody = 3;

    [GeneratedRegex(@"[.!?]+(?=\s|$)")]
    private static partial Regex SentenceTerminatorRegex();

    [Theory]
    [InlineData("Ona.HelpSweepBody")]
    [InlineData("Ona.HelpDipsBody")]
    public void OnaHelpBody_StaysWithinSentenceBudget(string key)
    {
        var en = LocalizationResourceLoader.Load(SupportedLanguage.English.Code);

        en.ContainsKey(key).ShouldBeTrue($"English localization is missing '{key}'");
        var sentences = SentenceTerminatorRegex().Matches(en[key]).Count;

        sentences.ShouldBeLessThanOrEqualTo(
            MaxSentencesPerHelpBody,
            $"{key} must stay at or below {MaxSentencesPerHelpBody} sentences so the flyout stays scannable");
    }
}
