using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Guards the localization contract of every <see cref="DesignIssue"/> produced by the
/// design validators: each <see cref="DesignIssueType"/> carries a non-null
/// <see cref="DesignIssue.LocalizationKey"/> that resolves in every shipped language,
/// and the English rendering matches the English <see cref="DesignIssue.Description"/>
/// so existing wording never regresses.
/// </summary>
public class DesignIssueLocalizationTests
{
    private static readonly string[] AllLanguages = { "en", "de", "es", "ja", "zh-Hans" };

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_HasNonNullLocalizationKey(DesignIssueType type)
    {
        var issue = ProduceIssueOfType(type);

        issue.LocalizationKey.ShouldNotBeNullOrWhiteSpace(
            $"DesignIssueType.{type} produced no LocalizationKey");
    }

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_KeyExistsInEveryShippedLanguage(DesignIssueType type)
    {
        var issue = ProduceIssueOfType(type);

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
        var issue = ProduceIssueOfType(type);
        var localization = CreateLocalization("en");

        var rendered = DesignIssueFormatter.Format(issue, localization);

        rendered.ShouldBe(issue.Description,
            $"English rendering for {type} diverged from Description");
    }

    [Theory]
    [MemberData(nameof(AllIssueTypes))]
    public void Issue_GermanRenderingDiffersFromEnglish(DesignIssueType type)
    {
        var issue = ProduceIssueOfType(type);
        var en = DesignIssueFormatter.Format(issue, CreateLocalization("en"));
        var de = DesignIssueFormatter.Format(issue, CreateLocalization("de"));

        de.ShouldNotBe(en, $"German rendering for {type} still shows the English text");
        de.ShouldNotBe(issue.LocalizationKey,
            $"German rendering for {type} leaked the localization key");
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

    /// <summary>Builds a fixture that produces exactly one issue of the requested type.</summary>
    private static DesignIssue ProduceIssueOfType(DesignIssueType type) => type switch
    {
        DesignIssueType.InvalidGeometry => ProduceConnectionIssue(p => p.IsInvalidGeometry = true),
        DesignIssueType.BlockedPath => ProduceConnectionIssue(p => p.IsBlockedFallback = true),
        DesignIssueType.BendRadiusBelowProcessMinimum =>
            ProduceConnectionIssue(p => p.ViolatesProcessMinBendRadius = true),
        DesignIssueType.StyledRouteThroughComponent =>
            ProduceConnectionIssue(p => p.PassesThroughComponent = true),
        DesignIssueType.OutOfBounds => ProduceOutOfBoundsIssue(),
        DesignIssueType.PdkProcessMismatch => ProducePdkMismatchIssue(),
        DesignIssueType.OverlappingPaths => ProduceOverlapIssue(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unhandled type")
    };

    private static DesignIssue ProduceConnectionIssue(Action<RoutedPath> mark)
    {
        var connection = CreateConnection();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        mark(path);
        connection.RestoreCachedPath(path);

        var issues = new DesignValidator().Validate(new[] { connection });
        return issues.Single();
    }

    private static DesignIssue ProduceOutOfBoundsIssue()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = -10;
        comp.PhysicalY = 0;
        comp.WidthMicrometers = 250;
        comp.HeightMicrometers = 250;

        var issues = new DesignValidator().ValidateComponentBounds(new[] { comp }, 5000, 5000);
        return issues.Single();
    }

    private static DesignIssue ProducePdkMismatchIssue()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "LockedComp";
        var sources = new Dictionary<Component, string?> { [comp] = "LockedLib" };

        var issues = new DesignValidator().ValidateComponentPdkCompatibility(
            new[] { comp }, sources, Array.Empty<string>(), Array.Empty<string>());
        return issues.Single();
    }

    private static DesignIssue ProduceOverlapIssue()
    {
        var conn1 = CreateConnectionWithSegment(0, 50, 100, 50);
        var conn2 = CreateConnectionWithSegment(50, 0, 50, 100);

        var issues = new WaveguideOverlapDetector().DetectOverlaps(
            new[] { conn1, conn2 }, Array.Empty<ComponentGroup>());
        return issues.Single();
    }

    private static WaveguideConnection CreateConnection()
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalPins.Add(new PhysicalPin
        {
            Name = "pin_a",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp1
        });

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = 100;
        comp2.PhysicalPins.Add(new PhysicalPin
        {
            Name = "pin_b",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp2
        });

        return new WaveguideConnection
        {
            StartPin = comp1.PhysicalPins.Last(),
            EndPin = comp2.PhysicalPins.Last()
        };
    }

    private static WaveguideConnection CreateConnectionWithSegment(
        double x1, double y1, double x2, double y2)
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = x1;
        comp1.PhysicalY = y1;
        comp1.PhysicalPins.Add(new PhysicalPin
        {
            Name = "out",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp1
        });

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = x2;
        comp2.PhysicalY = y2;
        comp2.PhysicalPins.Add(new PhysicalPin
        {
            Name = "in",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp2
        });

        var connection = new WaveguideConnection
        {
            StartPin = comp1.PhysicalPins.Last(),
            EndPin = comp2.PhysicalPins.Last()
        };

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        connection.RestoreCachedPath(path);
        return connection;
    }
}
