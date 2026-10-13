using System.Collections.ObjectModel;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Workspaces.ContextPanels;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// The left rail's Gates panel: lists only saved groups that are gates (top-level or nested
/// truth-table assignment), follows library changes, offers the installed starter gate
/// examples in ladder order, and forwards its actions.
/// </summary>
public class GatesPanelViewModelTests : IDisposable
{
    private readonly string _libraryDir = Path.Combine(Path.GetTempPath(), $"gates-panel-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_libraryDir))
            Directory.Delete(_libraryDir, true);
    }

    private ComponentLibraryViewModel EmptyLibrary()
    {
        Directory.CreateDirectory(_libraryDir);
        return new ComponentLibraryViewModel(new GroupLibraryManager(_libraryDir));
    }

    private static ComponentGroup Gate()
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

    private static GroupTemplate Template(string name, ComponentGroup? group, string source = "User") =>
        new() { Name = name, TemplateGroup = group, Source = source };

    [Fact]
    public void IsGate_TrueForTopLevelOrNestedAssignment_FalseOtherwise()
    {
        GatesPanelViewModel.IsGate(Template("gate", Gate())).ShouldBeTrue();
        GatesPanelViewModel.IsGate(Template("plain", LogicGateFixtureFactory.CreateCombinerGroup())).ShouldBeFalse();
        GatesPanelViewModel.IsGate(Template("empty", null)).ShouldBeFalse();

        var wrapper = new ComponentGroup("wrapper");
        wrapper.AddChild(Gate());
        GatesPanelViewModel.IsGate(Template("nested", wrapper)).ShouldBeTrue("a cell built from gates is offered too");
    }

    [Fact]
    public void SavedGates_ListOnlyGates_AndFollowTheLibrary()
    {
        var library = EmptyLibrary();
        var panel = new GatesPanelViewModel(library, new ObservableCollection<ExampleDesign>());
        panel.HasSavedGates.ShouldBeFalse();

        library.AddTemplate(Template("Plain", LogicGateFixtureFactory.CreateCombinerGroup()));
        library.AddTemplate(Template("My NAND", Gate()));
        library.AddTemplate(Template("PDK NOT", Gate(), source: "PDK"));

        panel.SavedGates.Select(g => g.Template.Name).ShouldBe(new[] { "My NAND", "PDK NOT" });
        panel.HasSavedGates.ShouldBeTrue();
    }

    [Fact]
    public void GateExamples_AreTheInstalledStarterGates_InLadderOrder()
    {
        var examples = new ObservableCollection<ExampleDesign>
        {
            new("Logic Gate OR-AND", Path.Combine("x", "Logic Gate OR-AND.lun")),
            new("Mach-Zehnder", Path.Combine("x", "Mach-Zehnder Interferometer.lun")),
            new("Logic Gate NOT-NAND", Path.Combine("x", "Logic Gate NOT-NAND.lun")),
        };
        var panel = new GatesPanelViewModel(EmptyLibrary(), examples);

        panel.GateExamples.Select(e => e.Name).ShouldBe(new[] { "Logic Gate NOT-NAND", "Logic Gate OR-AND" });
        panel.HasGateExamples.ShouldBeTrue();

        examples.Clear();
        panel.HasGateExamples.ShouldBeFalse("the list follows the Home examples refresh");
    }

    [Fact]
    public async Task Commands_ForwardToTheirHandlers()
    {
        var panel = new GatesPanelViewModel(EmptyLibrary(), new ObservableCollection<ExampleDesign>());
        string? opened = null;
        bool madeGate = false, showedLibrary = false;
        panel.OpenExampleRequested = path => { opened = path; return Task.FromResult(true); };
        panel.MakeGateRequested = () => madeGate = true;
        panel.ShowLibraryRequested = () => showedLibrary = true;

        await panel.OpenExampleCommand.ExecuteAsync(new ExampleDesign("NOT", "not.lun"));
        panel.MakeGateCommand.Execute(null);
        panel.ShowLibraryCommand.Execute(null);

        opened.ShouldBe("not.lun");
        madeGate.ShouldBeTrue();
        showedLibrary.ShouldBeTrue();
    }

    [Fact]
    public void StarterGateFiles_AreShippedExamples()
    {
        var dir = UnitTests.Integration.ExampleDesignFilesTests.ExamplesDirectory();
        foreach (var file in GatesPanelViewModel.GateExampleFiles)
            File.Exists(Path.Combine(dir, file)).ShouldBeTrue($"'{file}' must ship in examples/");
    }
}
