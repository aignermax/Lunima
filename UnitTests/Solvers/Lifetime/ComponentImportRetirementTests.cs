using System.Numerics;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_Core.Documents;
using CAP_DataAccess.Import;
using CAP_DataAccess.Persistence.PIR;
using Moq;
using Shouldly;

namespace UnitTests.Solvers.Lifetime;

public sealed class ComponentImportRetirementTests
{
    [AvaloniaTheory]
    [InlineData("picker", false)]
    [InlineData("importer", false)]
    [InlineData("mapping", false)]
    [InlineData("picker", true)]
    [InlineData("importer", true)]
    [InlineData("mapping", true)]
    public async Task SettingsImport_DrainsActualWorkAndRejectsObsoleteCommit(string stage, bool documentChanged)
    {
        var barrier = new RetirementBarrier();
        Task Pause(string current) => current == stage ? barrier.WaitAsync(CancellationToken.None) : Task.CompletedTask;
        var files = new Mock<IFileDialogService>();
        files.Setup(service => service.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(async () => { await Pause("picker"); return "fixture.s2p"; });
        var importer = new Mock<ISParameterImporter>();
        importer.SetupGet(service => service.SupportedExtensions).Returns(new[] { ".s2p" });
        importer.Setup(service => service.ImportAsync("fixture.s2p"))
            .Returns(async () => { await Pause("importer"); return Data(); });
        var mapping = new Mock<IPortMappingDialogService>();
        mapping.Setup(service => service.ShowAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(async () =>
            {
                await Pause("mapping");
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["a"] = "in", ["b"] = "out" };
            });
        var store = new Dictionary<string, ComponentSMatrixData>();
        var document = new DocumentSession();
        var changes = 0;
        var model = new ComponentSettingsDialogViewModel(files.Object, importers: [importer.Object], portMappingDialog: mapping.Object);
        model.Configure("component", "component", "Component", store,
            availablePinNames: ["in", "out"], document: document, onChanged: () => changes++);
        var running = model.LoadFromFileCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await barrier.AwaitStartAsync(running);
            if (documentChanged) document.Invalidate();
            else
            {
                stopping = model.StopAsync();
                stopping.IsCompleted.ShouldBeFalse("retirement must await the real dialog/import task");
            }
        }
        finally
        {
            await barrier.FinishAsync(running, stopping);
            await model.StopAsync();
            await document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        store.ShouldBeEmpty();
        changes.ShouldBe(0);
        model.IsImporting.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task RetiredSettings_DoNotOpenAnotherPicker()
    {
        var files = new Mock<IFileDialogService>();
        var model = new ComponentSettingsDialogViewModel(files.Object);
        model.Configure("component", "component", "Component", new());
        await model.StopAsync();
        await model.LoadFromFileCommand.ExecuteAsync(null);
        files.Invocations.ShouldBeEmpty();
    }

    private static ImportedSParameters Data() => new()
    {
        PortCount = 2, PortNames = ["a", "b"],
        SMatricesByWavelengthNm = new() { [1550] = new Complex[,] { { 0, .95 }, { .95, 0 } } },
    };
}
