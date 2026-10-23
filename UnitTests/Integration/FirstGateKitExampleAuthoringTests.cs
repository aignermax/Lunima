using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP_Core.Components.Core;
using CAP_Core;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Authoring utility for the gate kit <c>examples/Logic Gate - Build your first gate.lun</c>
/// that the "Your first gate" tour opens: loads the shipped NOT/NAND gate, ungroups it
/// (the five optical parts stay placed and wired, the gate assignment is gone) and writes
/// the result through the real save command.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and <c>Category=Slow</c>.
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class FirstGateKitExampleAuthoringTests
{
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";
    private const string SourceExampleFileName = "Logic Gate NOT-NAND.lun";

    [Fact]
    public async Task Author_FirstGateKit_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var templates = TestPdkLoader.LoadAllTemplates();
        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas, templates);
        var source = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), SourceExampleFileName);
        (await fileOps.LoadDesignFromPathAsync(source)).ShouldBeTrue();

        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        new UngroupCommand(canvas, group).Execute();
        canvas.Components.Any(c => c.Component is ComponentGroup).ShouldBeFalse();
        TrimInputStubs(canvas);

        var target = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), FirstGateTourViewModel.GateKitExampleFileName);
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(target);
        fileOps.FileDialogService = dialog.Object;
        await fileOps.SaveDesignAsCommand.ExecuteAsync(null);

        File.Exists(target).ShouldBeTrue();
    }

    /// <summary>
    /// The gate's two 100 µm input stubs sit 8 µm apart (the MMI input pitch) but are
    /// 10 µm tall, so as loose parts their footprints overlap. The kit drops them and
    /// slides the combiner MMI onto the west edge instead: its in1/in2 become inputs A
    /// and B directly, beside the phase shifter's bias input, and its out1 reaches the
    /// recombiner over a longer straight (lengths carry no phase in the default model).
    /// </summary>
    private static void TrimInputStubs(DesignCanvasViewModel canvas)
    {
        foreach (var id in new[] { "input_a", "input_b" })
            canvas.RemoveComponent(canvas.Components.Single(c => c.Component.Identifier == id));

        var combine = canvas.Components.Single(c => c.Component.Identifier == "combine");
        var recombine = canvas.Components.Single(c => c.Component.Identifier == "recombine").Component;
        var phase = canvas.Components.Single(c => c.Component.Identifier == "phase").Component;
        canvas.RemoveConnectionsForPin(combine.Component.PhysicalPins.Single(p => p.Name == "out1"));
        canvas.MoveComponent(combine, phase.PhysicalX - combine.Component.PhysicalX, 0);
        combine.Component.PhysicalX.ShouldBe(phase.PhysicalX, "the combiner must land on the west edge");

        var start = combine.Component.PhysicalPins.Single(p => p.Name == "out1");
        var end = recombine.PhysicalPins.Single(p => p.Name == "in1");
        var route = canvas.Router.Route(start, end);
        route.IsValid.ShouldBeTrue("the combiner → recombiner straight must route");
        var link = canvas.ConnectPinsWithCachedRoute(start, end, route);
        link.ShouldNotBeNull();
        link!.Connection.IsRouteFrozen = true;
    }

    private static FileOperationsViewModel CreateFileOperations(
        DesignCanvasViewModel canvas, IEnumerable<CAP.Avalonia.ViewModels.Library.ComponentTemplate> templates) =>
        new(new CAP_DataAccess.Persistence.NativeDesign.NativeDesignFileStore(),
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<CAP.Avalonia.ViewModels.Library.ComponentTemplate>(templates),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
}
