using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.ViewModels;

/// <summary>
/// Visibility contract of the (?) crossing help button (#1391):
/// <see cref="DesignValidationViewModel.HasWaveguideCrossingIssue"/> — and with it the
/// button in the Design Checks tab — is true exactly while the findings list contains a
/// <see cref="DesignIssueType.WaveguideCrossing"/> finding.
/// </summary>
public class DesignValidationViewModelCrossingHelpTests
{
    private readonly DesignValidationViewModel _vm = new();

    [Fact]
    public void RunValidation_WithCrossingPair_ShowsCrossingHelp()
    {
        _vm.RunValidation(new[]
        {
            CreateConnection("compA", "compB", 0, 50, 100, 50),
            CreateConnection("compC", "compD", 50, 0, 50, 100),
        });

        _vm.HasWaveguideCrossingIssue.ShouldBeTrue();
        _vm.Issues.ShouldContain(i => i.Type == DesignIssueType.WaveguideCrossing);
    }

    [Fact]
    public void RunValidation_WithoutCrossingPair_HidesCrossingHelp()
    {
        _vm.RunValidation(new[]
        {
            CreateConnection("compA", "compB", 0, 0, 100, 0),
            CreateConnection("compC", "compD", 0, 10, 100, 10),
        });

        _vm.HasWaveguideCrossingIssue.ShouldBeFalse();
    }

    [Fact]
    public void RunValidation_RerunWithoutCrossing_ResetsCrossingHelp()
    {
        _vm.RunValidation(new[]
        {
            CreateConnection("compA", "compB", 0, 50, 100, 50),
            CreateConnection("compC", "compD", 50, 0, 50, 100),
        });
        _vm.HasWaveguideCrossingIssue.ShouldBeTrue();

        _vm.RunValidation(new[]
        {
            CreateConnection("compE", "compF", 0, 0, 100, 0),
        });

        _vm.HasWaveguideCrossingIssue.ShouldBeFalse();
    }

    private static WaveguideConnection CreateConnection(
        string startComponentId, string endComponentId,
        double x1, double y1, double x2, double y2)
    {
        var startPin = AddPin(CreateComponent(startComponentId), "out");
        var endPin = AddPin(CreateComponent(endComponentId), "in");
        var connection = new WaveguideConnection { StartPin = startPin, EndPin = endPin };
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static Component CreateComponent(string identifier)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = identifier;
        return component;
    }

    private static PhysicalPin AddPin(Component component, string name)
    {
        var pin = new PhysicalPin
        {
            Name = name,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = component
        };
        component.PhysicalPins.Add(pin);
        return pin;
    }
}
