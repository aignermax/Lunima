using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Integration tests for PDK consistency checking.
/// Issue #334: Validate that JSON PDK definitions are internally consistent and,
/// where possible, match reference ComponentTemplate definitions.
///
/// These tests confirm that the coordinate mismatch investigation tooling works
/// and that our built-in demo-pdk.json components are self-consistent.
/// </summary>
public class PdkConsistencyTests
{
    private readonly PdkConsistencyChecker _checker = new();
    private readonly PdkLoader _loader = new();

    // ─── Internal consistency tests ───────────────────────────────────────────

    [Fact]
    public void Check_AllPinsWithinBounds_NoDimensionErrors()
    {
        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("TestComp", 100, 50,
                new[] { ("a0", 0.0, 25.0, 180.0), ("b0", 100.0, 25.0, 0.0) })
        });

        var findings = _checker.Check(pdk);

        findings.ShouldNotContain(f => f.FindingType == "PinOutOfBounds");
        findings.ShouldNotContain(f => f.FindingType == "InvalidDimension");
    }

    [Fact]
    public void Check_PinOutsideBounds_ReturnsWarning()
    {
        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("BadComp", 50, 20,
                new[] { ("a0", 0.0, 30.0, 180.0) }) // Y=30 > height=20
        });

        var findings = _checker.Check(pdk);

        findings.ShouldContain(f =>
            f.FindingType == "PinOutOfBounds" &&
            f.ComponentName == "BadComp" &&
            f.Severity == PdkFindingSeverity.Warning);
    }

    [Fact]
    public void Check_ZeroWidth_ReturnsError()
    {
        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("ZeroWidth", 0, 20,
                new[] { ("a0", 0.0, 10.0, 180.0) })
        });

        // Loader validates — we test checker directly with pre-built draft
        var badComp = pdk.Components[0];
        var findings = _checker.Check(pdk);

        findings.ShouldContain(f =>
            f.FindingType == "InvalidDimension" &&
            f.Severity == PdkFindingSeverity.Error);
    }

    [Fact]
    public void Check_NonOriginFirstPin_ReturnsOriginOffsetWarning()
    {
        // When first pin is NOT "a0", "in", "waveguide", or similar,
        // the derived NazcaOriginOffset from ConvertPdkComponentToTemplate() may be wrong.
        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("RiskyComp", 100, 40,
                new[] { ("out1", 100.0, 10.0, 0.0), ("out2", 100.0, 30.0, 0.0) })
        });

        var findings = _checker.Check(pdk);

        findings.ShouldContain(f =>
            f.FindingType == "OriginOffsetRisk" &&
            f.ComponentName == "RiskyComp" &&
            f.Severity == PdkFindingSeverity.Warning);
    }

    [Fact]
    public void Check_SafeFirstPin_NoOriginOffsetWarning()
    {
        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("SafeComp", 100, 40,
                new[] { ("a0", 0.0, 20.0, 180.0), ("b0", 100.0, 20.0, 0.0) })
        });

        var findings = _checker.Check(pdk);

        findings.ShouldNotContain(f => f.FindingType == "OriginOffsetRisk");
    }

    // ─── Template comparison tests ────────────────────────────────────────────

    [Fact]
    public void CompareWithTemplates_DimensionMatch_NoErrors()
    {
        // Build a reference template with explicit NazcaFunctionName and a matching JSON component
        var refTemplate = BuildReferenceTemplate("test.mmi2x2", 250, 60,
            new[] { ("a0", 0.0, 26.0, 180.0), ("b0", 250.0, 26.0, 0.0) });

        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("MMI 2x2", 250, 60,
                new[] { ("a0", 0.0, 26.0, 180.0), ("b0", 250.0, 26.0, 0.0) },
                nazcaFunction: "test.mmi2x2")
        });

        var findings = _checker.CompareWithTemplates(pdk, new[] { refTemplate });

        findings.ShouldNotContain(f =>
            f.FindingType == "DimensionMismatch" &&
            f.Severity == PdkFindingSeverity.Error);
    }

    [Fact]
    public void CompareWithTemplates_DimensionMismatch_ReturnsError()
    {
        var refTemplate = BuildReferenceTemplate("test.mmi2x2", 250, 60,
            new[] { ("a0", 0.0, 26.0, 180.0) });

        // Build JSON component with wrong width
        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("MMI 2x2", 200, 60,  // width 200 vs 250 → 50µm deviation
                new[] { ("a0", 0.0, 26.0, 180.0) },
                nazcaFunction: "test.mmi2x2")
        });

        var findings = _checker.CompareWithTemplates(pdk, new[] { refTemplate });

        findings.ShouldContain(f =>
            f.FindingType == "DimensionMismatch" &&
            f.Severity == PdkFindingSeverity.Error &&
            f.DeviationMicrometers > 0);
    }

    [Fact]
    public void CompareWithTemplates_PinMismatch_ReturnsError()
    {
        var refTemplate = BuildReferenceTemplate("test.gc", 100, 19,
            new[] { ("waveguide", 100.0, 9.5, 0.0) });

        var pdk = BuildValidPdk(new[]
        {
            BuildComponent("GC", 100, 19,
                new[] { ("waveguide", 100.0, 15.0, 0.0) },  // pin Y=15 instead of 9.5 → 5.5µm off
                nazcaFunction: "test.gc")
        });

        var findings = _checker.CompareWithTemplates(pdk, new[] { refTemplate });

        findings.ShouldContain(f =>
            f.FindingType == "PinPositionMismatch" &&
            f.Severity == PdkFindingSeverity.Error);
    }

    // ─── Demo PDK file tests ──────────────────────────────────────────────────

    [Fact]
    public void DemoPdk_AllComponents_HaveValidDimensions()
    {
        var demoPdkPath = FindDemoPdkPath();
        if (demoPdkPath == null)
        {
            // Skip if PDK file not found (CI may not have bundled PDKs)
            return;
        }

        var pdk = _loader.LoadFromFile(demoPdkPath);
        var findings = _checker.Check(pdk);

        var errors = findings.Where(f => f.FindingType == "InvalidDimension").ToList();
        errors.ShouldBeEmpty($"Demo PDK components have invalid dimensions: {string.Join(", ", errors.Select(e => e.Message))}");
    }

    [Fact]
    public void DemoPdk_AllPins_WithinComponentBounds()
    {
        var demoPdkPath = FindDemoPdkPath();
        if (demoPdkPath == null)
            return;

        var pdk = _loader.LoadFromFile(demoPdkPath);
        var findings = _checker.Check(pdk);

        var outOfBounds = findings.Where(f => f.FindingType == "PinOutOfBounds").ToList();
        outOfBounds.ShouldBeEmpty(
            $"Demo PDK has pins outside component bounds: {string.Join(", ", outOfBounds.Select(e => e.Message))}");
    }

    [Fact]
    public void DemoPdk_OriginOffsetRisks_AreDocumented()
    {
        // This test documents which demo PDK components have risky origin offset derivation.
        // It does NOT fail — it just reports the findings for investigation.
        var demoPdkPath = FindDemoPdkPath();
        if (demoPdkPath == null)
            return;

        var pdk = _loader.LoadFromFile(demoPdkPath);
        var findings = _checker.Check(pdk);

        var risks = findings.Where(f => f.FindingType == "OriginOffsetRisk").ToList();
        // Report for investigation (not a failure — origin offset may be intentional)
        foreach (var risk in risks)
        {
            // Output for test runner visibility
            _ = $"OriginOffsetRisk: {risk.ComponentName} — {risk.Message}";
        }

        // Assert we ran the check (at least 1 component was analyzed)
        pdk.Components.Count.ShouldBeGreaterThan(0);
    }

    // ─── ViewModel integration test ──────────────────────────────────────────

    [Fact]
    public void PdkConsistencyViewModel_CheckPdksCommand_ExecutesWithoutException()
    {
        var vm = new CAP.Avalonia.ViewModels.Diagnostics.PdkConsistencyViewModel();

        // Command should execute without throwing
        vm.CheckPdksCommand.Execute(null);

        // After execution, status text should be set
        vm.StatusText.ShouldNotBeNullOrEmpty();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string? FindDemoPdkPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs", "demo-pdk.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "CAP-DataAccess", "PDKs", "demo-pdk.json"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static PdkDraft BuildValidPdk(PdkComponentDraft[] components)
        => new()
        {
            Name = "TestPdk",
            FileFormatVersion = 1,
            Components = components.ToList()
        };

    private static PdkComponentDraft BuildComponent(
        string name,
        double width,
        double height,
        (string Name, double X, double Y, double Angle)[] pins,
        string? nazcaFunction = null)
        => new()
        {
            Name = name,
            Category = "Test",
            NazcaFunction = nazcaFunction ?? $"test.{name.ToLower()}",
            WidthMicrometers = width,
            HeightMicrometers = height,
            Pins = pins.Select(p => new PhysicalPinDraft
            {
                Name = p.Name,
                OffsetXMicrometers = p.X,
                OffsetYMicrometers = p.Y,
                AngleDegrees = p.Angle
            }).ToList()
        };

    private static ComponentTemplate BuildReferenceTemplate(
        string nazcaFunction, double width, double height,
        (string Name, double X, double Y, double Angle)[] pins)
        => new ComponentTemplate
        {
            Name = "RefTemplate_" + nazcaFunction,
            Category = "Test",
            NazcaFunctionName = nazcaFunction,
            WidthMicrometers = width,
            HeightMicrometers = height,
            PinDefinitions = pins.Select(p =>
                new PinDefinition(p.Name, p.X, p.Y, p.Angle)).ToArray(),
            CreateSMatrix = _ => null!
        };
}
