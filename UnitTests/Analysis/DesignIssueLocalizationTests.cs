using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Guards the localization contract of every <see cref="DesignIssue"/> produced by the
/// design validators: each <see cref="DesignIssueType"/> carries a non-null
/// <see cref="DesignIssue.LocalizationKey"/> that resolves in every shipped language,
/// and the English rendering matches the English <see cref="DesignIssue.Description"/>
/// so existing wording never regresses. Issues are produced through the real checkers
/// (see <see cref="DesignIssueFixtures"/>), never hand-built.
/// </summary>
public class DesignIssueLocalizationTests
{
    private static readonly string[] AllLanguages = { "en", "de", "es", "ja", "zh-Hans" };

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_HasNonNullLocalizationKey(DesignIssueType type)
    {
        var issue = DesignIssueFixtures.Produce(type);

        issue.LocalizationKey.ShouldNotBeNullOrWhiteSpace(
            $"DesignIssueType.{type} produced no LocalizationKey");
    }

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_KeyExistsInEveryShippedLanguage(DesignIssueType type)
    {
        var issue = DesignIssueFixtures.Produce(type);

        foreach (var code in AllLanguages)
        {
            var table = LocalizationResourceLoader.Load(code);
            table.ContainsKey(issue.LocalizationKey!).ShouldBeTrue(
                $"key '{issue.LocalizationKey}' for {type} missing in strings-{code}.json");
        }
    }

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_EnglishRenderingMatchesDescription(DesignIssueType type)
    {
        var issue = DesignIssueFixtures.Produce(type);
        var localization = CreateLocalization("en");

        var rendered = DesignIssueFormatter.Format(issue, localization);

        rendered.ShouldBe(issue.Description,
            $"English rendering for {type} diverged from Description");
    }

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_GermanRenderingDiffersFromEnglish(DesignIssueType type)
    {
        var issue = DesignIssueFixtures.Produce(type);
        var en = DesignIssueFormatter.Format(issue, CreateLocalization("en"));
        var de = DesignIssueFormatter.Format(issue, CreateLocalization("de"));

        de.ShouldNotBe(en, $"German rendering for {type} still shows the English text");
        de.ShouldNotBe(issue.LocalizationKey,
            $"German rendering for {type} leaked the localization key");
    }

    [Theory]
    [InlineData(RoutingFailureReason.None, "DesignChecks.BlockedPath")]
    [InlineData(RoutingFailureReason.Contention, "DesignChecks.BlockedPath.Contention")]
    [InlineData(RoutingFailureReason.EndpointBlocked, "DesignChecks.BlockedPath.EndpointBlocked")]
    public void BlockedPath_FailureReasonSelectsKey_AndResolvesEverywhere(
        RoutingFailureReason reason, string expectedKey)
    {
        var issue = DesignIssueFixtures.ProduceBlockedPath(reason);

        issue.LocalizationKey.ShouldBe(expectedKey);
        foreach (var code in AllLanguages)
        {
            LocalizationResourceLoader.Load(code).ContainsKey(expectedKey).ShouldBeTrue(
                $"key '{expectedKey}' missing in strings-{code}.json");
        }
        DesignIssueFormatter.Format(issue, CreateLocalization("en")).ShouldBe(issue.Description);
    }

    [Fact]
    public void Format_IssueWithoutKey_FallsBackToDescription()
    {
        var issue = new DesignIssue(
            DesignIssueType.InvalidGeometry, connection: null, x: 0, y: 0,
            description: "plain fallback");

        DesignIssueFormatter.Format(issue, CreateLocalization("de")).ShouldBe("plain fallback");
    }

    [Fact]
    public void Format_MissingKey_FallsBackToDescription()
    {
        var issue = new DesignIssue(
            DesignIssueType.InvalidGeometry, connection: null, x: 0, y: 0,
            description: "plain fallback",
            localizationKey: "DesignChecks.DoesNotExist",
            localizationArgs: new object[] { "a", "b" });

        DesignIssueFormatter.Format(issue, CreateLocalization("en")).ShouldBe("plain fallback");
    }

    public static IEnumerable<object[]> AllIssueTypes()
    {
        foreach (DesignIssueType type in Enum.GetValues<DesignIssueType>())
            yield return new object[] { type };
    }

    private static LocalizationService CreateLocalization(string languageCode)
    {
        var service = new LocalizationService();
        service.SetLanguage(languageCode);
        return service;
    }
}
