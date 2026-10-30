using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using static UnitTests.PdkOffset.NazcaPreviewViewModelFixture;

namespace UnitTests.PdkOffset;

/// <summary>Automatic calibration pairs ports by geometry and direction.</summary>
[Collection("NazcaComponentPreviewService")]
public class NazcaComponentPreviewPinMatchingTests
{
    // Keep localized status assertions independent of the developer or CI language.
    public NazcaComponentPreviewPinMatchingTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void AutoCalibrate_AngleAware_DoesNotCrossPairSymmetricCrossingPorts()
    {
        // Crossing 4-Port has 4 ports, each at the centre of one bbox edge.
        // After a wrong starting calibration the 4 Lunima projections land
        // roughly equidistant from each Nazca pin. Without the angle term,
        // greedy nearest can cross-pair (left-pointing pin matches the right
        // edge etc). The angle term must dominate the position tie.
        var draft = new PdkComponentDraft
        {
            Name = "crossing", NazcaFunction = "ebeam_crossing4",
            // Calibration starts roughly correct but with two pin POSITIONS swapped —
            // simulating the corrupted-state-from-buggy-prior-run scenario.
            WidthMicrometers = 9.7, HeightMicrometers = 9.7,
            NazcaOriginOffsetX = 4.85, NazcaOriginOffsetY = 4.85,
            Pins = new List<PhysicalPinDraft>
            {
                // port 1 angle 180 (Left in both conventions) but parked at the RIGHT edge
                new() { Name = "port 1", OffsetXMicrometers = 9.65, OffsetYMicrometers = 4.85, AngleDegrees = 180 },
                // port 2 angle 0 (Right in both conventions) but parked at the LEFT edge
                new() { Name = "port 2", OffsetXMicrometers = 0.05, OffsetYMicrometers = 4.85, AngleDegrees = 0   },
                // port 3 angle 90 in Lunima Y-down = stub points DOWN visually = away from box body at the BOTTOM edge
                new() { Name = "port 3", OffsetXMicrometers = 4.85, OffsetYMicrometers = 9.65, AngleDegrees = 90  },
                // port 4 angle 270 in Lunima Y-down = stub points UP visually = away from box body at the TOP edge
                new() { Name = "port 4", OffsetXMicrometers = 4.85, OffsetYMicrometers = 0.05, AngleDegrees = 270 },
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = -4.85, YMin = -4.85, XMax = 4.85, YMax = 4.85,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "left",   X = -4.85, Y = 0,     Angle = 180 },
                new() { Name = "right",  X =  4.85, Y = 0,     Angle = 0   },
                new() { Name = "bottom", X = 0,     Y = -4.85, Angle = 270 },
                new() { Name = "top",    X = 0,     Y =  4.85, Angle = 90  },
            }
        };

        PdkOffsetCalibration.ApplyAutoCalibrate(draft, result)
            .ShouldBe(AutoCalibrateOutcome.Success);

        // After calibration each Lunima pin should sit at the edge that
        // matches its angle, not be silently permuted onto another pin.
        var p1 = draft.Pins.First(p => p.Name == "port 1");
        var p2 = draft.Pins.First(p => p.Name == "port 2");
        var p3 = draft.Pins.First(p => p.Name == "port 3");
        var p4 = draft.Pins.First(p => p.Name == "port 4");
        p1.AngleDegrees.ShouldBe(180);                          // Nazca 180 → Lunima 180 (invariant)
        p1.OffsetXMicrometers.ShouldBe(0, tolerance: 0.01);     // left edge
        p2.AngleDegrees.ShouldBe(0);                            // Nazca 0   → Lunima 0   (invariant)
        p2.OffsetXMicrometers.ShouldBe(9.7, tolerance: 0.01);   // right edge
        p3.AngleDegrees.ShouldBe(90);                           // Nazca 270 → Lunima 90  (Y-flip)
        p3.OffsetYMicrometers.ShouldBe(9.7, tolerance: 0.01);   // bottom edge in Y-down
        p4.AngleDegrees.ShouldBe(270);                          // Nazca 90  → Lunima 270 (Y-flip)
        p4.OffsetYMicrometers.ShouldBe(0, tolerance: 0.01);     // top edge in Y-down
    }

    [Fact]
    public void AutoCalibrate_MatchesPinsByNearestNotByOrder()
    {
        // Lunima order: [in (left-bottom), out (right-top)].
        // Nazca order:  [opt1 (right-top), opt2 (left-bottom)] — REVERSED.
        // Greedy nearest-neighbour must pair in↔opt2 and out↔opt1, not by index.
        var draft = new PdkComponentDraft
        {
            Name = "reversed", NazcaFunction = "pdk.rev",
            WidthMicrometers = 10, HeightMicrometers = 10,
            NazcaOriginOffsetX = 0, NazcaOriginOffsetY = 0,
            Pins = new List<PhysicalPinDraft>
            {
                new() { Name = "in",  OffsetXMicrometers = 0,  OffsetYMicrometers = 10 },
                new() { Name = "out", OffsetXMicrometers = 10, OffsetYMicrometers = 0 }
            }
        };
        var result = new NazcaPreviewResult
        {
            Success = true,
            XMin = 0, YMin = 0, XMax = 10, YMax = 10,
            Pins = new List<NazcaPreviewPin>
            {
                new() { Name = "opt1", X = 10, Y = 10 },  // matches 'out'
                new() { Name = "opt2", X = 0,  Y = 0  }   // matches 'in'
            }
        };

        var vm = BuildVmWithSelection(draft, result);
        vm.AutoCalibrateCommand.Execute(null);

        // 'in' must end up at (0, 10) (Nazca (0,0) → top-left), 'out' at (10,0)
        var inPin  = draft.Pins.First(p => p.Name == "in");
        var outPin = draft.Pins.First(p => p.Name == "out");
        inPin.OffsetXMicrometers.ShouldBe(0.0,  tolerance: 0.001);
        inPin.OffsetYMicrometers.ShouldBe(10.0, tolerance: 0.001);
        outPin.OffsetXMicrometers.ShouldBe(10.0, tolerance: 0.001);
        outPin.OffsetYMicrometers.ShouldBe(0.0,  tolerance: 0.001);
    }
}
