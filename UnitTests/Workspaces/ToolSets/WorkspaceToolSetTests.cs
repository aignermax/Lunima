using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.ToolSets;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Workspaces.ToolSets;

/// <summary>
/// Each workspace's tool set decides what it places: Layout everything, Optics instruments
/// only, Logic gates only, System nothing — and only Layout and Optics offer the component editor.
/// </summary>
public class WorkspaceToolSetTests
{
    private static ComponentTemplate T(string name, string category = "Test") => new() { Name = name, Category = category };

    internal static ComponentGroup Gate()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        group.TruthTablePinAssignment = new TruthTablePinAssignment
        {
            InputPinNames = new List<string> { "a", "b" },
            OutputPinNames = new List<string> { "y" },
            BiasPinNames = new List<string>(),
            Threshold = 0.25,
        };
        return group;
    }

    private static GroupTemplate G(string name, ComponentGroup group) => new() { Name = name, TemplateGroup = group };

    [Theory]
    [InlineData("Grating Coupler TE 1550", true)]
    [InlineData("Edge Coupler", true)]
    [InlineData("Laser Source", true)]
    [InlineData("Photodetector", true)]
    [InlineData("Terminator", true)]
    [InlineData("ONA Analyzer", true)]
    [InlineData("MMI 2x2", false)]
    [InlineData("Directional Coupler", false)]
    [InlineData("Straight Waveguide", false)]
    [InlineData("Phase Shifter", false)]
    public void IsInstrument_ClassifiesSourcesDetectorsAndAnalyzers(string name, bool expected)
    {
        InstrumentTemplates.IsInstrument(T(name)).ShouldBe(expected);
    }

    [Fact]
    public void IsInstrument_TrustsTheAnalysisCategory()
    {
        InstrumentTemplates.IsInstrument(T("Measurement", InstrumentTemplates.AnalysisCategory)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(Workspace.Layout, true, true)]
    [InlineData(Workspace.Optics, true, false)]
    [InlineData(Workspace.Logic, false, false)]
    [InlineData(Workspace.System, false, false)]
    public void ComponentPlacement_FollowsTheWorkspace(Workspace workspace, bool instrument, bool circuitPart)
    {
        var toolSet = WorkspaceProfile.For(workspace).ToolSet;

        toolSet.CanPlace(T("Grating Coupler TE 1550")).ShouldBe(instrument);
        toolSet.CanPlace(T("MMI 2x2")).ShouldBe(circuitPart);
    }

    [Theory]
    [InlineData(Workspace.Layout, true, true)]
    [InlineData(Workspace.Optics, false, false)]
    [InlineData(Workspace.Logic, true, false)]
    [InlineData(Workspace.System, false, false)]
    public void GroupPlacement_FollowsTheWorkspace(Workspace workspace, bool gate, bool plainGroup)
    {
        var toolSet = WorkspaceProfile.For(workspace).ToolSet;

        toolSet.CanPlace(G("NAND", Gate())).ShouldBe(gate);
        toolSet.CanPlace(G("Splitter tree", LogicGateFixtureFactory.CreateCombinerGroup())).ShouldBe(plainGroup);
    }

    [Theory]
    [InlineData(Workspace.Layout, LibraryScope.AllComponents, true)]
    [InlineData(Workspace.Optics, LibraryScope.Instruments, true)]
    [InlineData(Workspace.Logic, LibraryScope.Gates, false)]
    [InlineData(Workspace.System, LibraryScope.None, false)]
    public void EachProfile_CarriesItsToolSet(Workspace workspace, LibraryScope scope, bool componentEditor)
    {
        var toolSet = WorkspaceProfile.For(workspace).ToolSet;

        toolSet.Library.ShouldBe(scope);
        toolSet.AllowsComponentEditor.ShouldBe(componentEditor);
        toolSet.RailSections.ShouldNotBeEmpty();
    }
}
