using CAP.Avalonia.ViewModels.Canvas.ViewSettings;
using CAP_Core.Analysis;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas;

/// <summary>The workspace-driven canvas view settings: check-marker findings and their toggle.</summary>
public class CanvasViewSettingsTests
{
    private static DesignIssue Hint() =>
        new(DesignIssueType.UnconnectedPin, null, 10, 20, "open pin");

    [Fact]
    public void Defaults_GeometryEmphasis_MarkersOn_NoFindings()
    {
        var settings = new CanvasViewSettings();

        settings.Emphasis.ShouldBe(CanvasEmphasis.Geometry);
        settings.ShowDesignCheckMarkers.ShouldBeTrue();
        settings.HasDesignCheckFindings.ShouldBeFalse();
    }

    [Fact]
    public void SetFindings_ThenClear_RaisesChangesForTheCanvasRepaint()
    {
        var settings = new CanvasViewSettings();
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.SetDesignCheckFindings([Hint()]);
        settings.HasDesignCheckFindings.ShouldBeTrue();
        settings.ClearDesignCheckFindings();

        settings.HasDesignCheckFindings.ShouldBeFalse();
        changed.Count(p => p == nameof(CanvasViewSettings.DesignCheckFindings)).ShouldBe(2);
    }

    [Fact]
    public void Clear_WithoutFindings_IsSilent()
    {
        var settings = new CanvasViewSettings();
        var raised = false;
        settings.PropertyChanged += (_, _) => raised = true;

        settings.ClearDesignCheckFindings();

        raised.ShouldBeFalse();
    }

    [Fact]
    public void ToggleCommand_FlipsTheMarkers()
    {
        var settings = new CanvasViewSettings();

        settings.ToggleDesignCheckMarkersCommand.Execute(null);
        settings.ShowDesignCheckMarkers.ShouldBeFalse();
        settings.ToggleDesignCheckMarkersCommand.Execute(null);
        settings.ShowDesignCheckMarkers.ShouldBeTrue();
    }
}
