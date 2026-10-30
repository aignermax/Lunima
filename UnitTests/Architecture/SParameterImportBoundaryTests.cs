using System.Numerics;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_DataAccess.Import;
using CAP_DataAccess.Persistence.PIR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using static UnitTests.Architecture.SParameterImportBoundaryFixture;

namespace UnitTests.Architecture;

public sealed class SParameterImportBoundaryTests
{
    [Fact]
    public async Task ApplicationComposition_ProvidesOneConfiguredImportCatalog()
    {
        var services = new ServiceCollection();
        CAP.Avalonia.App.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<ISParameterImportCatalog>();
        catalog.ShouldBeSameAs(provider.GetRequiredService<SParameterImportCatalog>());
        provider.GetRequiredService<ISParameterImportCatalog>().ShouldBeSameAs(catalog);
        var configured = provider.GetServices<ISParameterImporter>().ToArray();
        configured.ShouldNotBeEmpty();
        (await catalog.FindAsync("virtual.sparam", SParameterImportDetection.ExtensionOnly))
            .ShouldBeSameAs(configured.Single(value => value.SupportedExtensions.Contains(".sparam")));
        (await catalog.FindAsync("virtual.s2p", SParameterImportDetection.ExtensionOnly))
            .ShouldBeSameAs(configured.Single(value => value.SupportedExtensions.Contains(".s2p")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_UsesConfiguredParser_AndDrainsBeforeRetiring(bool retire)
    {
        var parser = new HeldImporter();
        var catalog = new Catalog(parser);
        var stored = new Dictionary<string, ComponentSMatrixData>();
        await using var vm = new ComponentSettingsDialogViewModel(catalog, Picker("virtual.configured"));
        vm.Configure("component", "matrix", "Configured component", stored);
        var running = vm.LoadFromFileCommand.ExecuteAsync(null);
        try
        {
            await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            catalog.Requests.ShouldBe(new[] { ("virtual.configured", SParameterImportDetection.VerifyAmbiguousText) });
            parser.Path.ShouldBe("virtual.configured");
            vm.IsImporting.ShouldBeTrue();
            stored.ShouldBeEmpty();
            var stopping = retire ? vm.StopAsync() : Task.CompletedTask;
            if (retire) stopping.IsCompleted.ShouldBeFalse();
            parser.Finish.TrySetResult(Model());
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            if (retire) stored.ShouldBeEmpty();
            else AssertStored(stored["matrix"], ["port 1", "port 2"]);
        }
        finally
        {
            parser.Finish.TrySetResult(Model());
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_UsesConfiguredParser_AndDrainsBeforeRetiring(bool retire)
    {
        var parser = new HeldImporter();
        var catalog = new Catalog(parser);
        await using var vm = Editor(catalog);
        var running = vm.LoadSMatrixFromFileCommand.ExecuteAsync(null);
        try
        {
            await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            catalog.Requests.ShouldBe(new[] { ("virtual.configured", SParameterImportDetection.ExtensionOnly) });
            vm.IsBusy.ShouldBeTrue();
            vm.HasSMatrix.ShouldBeFalse();
            var stopping = retire ? vm.StopAsync() : Task.CompletedTask;
            if (retire) stopping.IsCompleted.ShouldBeFalse();
            parser.Finish.TrySetResult(Model());
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            if (retire) vm.SMatrixEntries.ShouldBeEmpty();
            else
            {
                vm.HasSMatrix.ShouldBeTrue();
                var row = vm.SMatrixEntries.ShouldHaveSingleItem();
                row.WavelengthKey.ShouldBe("1550");
                row.PortNamesDisplay.ShouldBe("in, out");
                row.Dimensions.ShouldBe("2 × 2");
            }
        }
        finally
        {
            parser.Finish.TrySetResult(Model());
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("names")]
    [InlineData("matrix")]
    [InlineData("source")]
    public async Task Settings_CapturesImportedValues_BeforeAwaitingPortMapping(string mutation)
    {
        var source = Model();
        var parser = new HeldImporter();
        parser.Finish.SetResult(source);
        var mappingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishMapping = new TaskCompletionSource<IReadOnlyDictionary<string, string>?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new Mock<IPortMappingDialogService>();
        mapping.Setup(value => value.ShowAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(() => { mappingEntered.TrySetResult(); return finishMapping.Task; });
        var stored = new Dictionary<string, ComponentSMatrixData>();
        await using var vm = new ComponentSettingsDialogViewModel(new Catalog(parser), Picker("virtual.sparam"),
            portMappingDialog: mapping.Object);
        vm.Configure("component", "matrix", "Mapped component", stored, availablePinNames: ["in", "out"]);
        var running = vm.LoadFromFileCommand.ExecuteAsync(null);
        try
        {
            await mappingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stored.ShouldBeEmpty();
            if (mutation == "names") source.PortNames[0] = "mutated provider name";
            if (mutation == "matrix") source.SMatricesByWavelengthNm[1550][0, 1] = new Complex(99, 88);
            if (mutation == "source") source.SourceFormat = "mutated provider format";
            finishMapping.SetResult(new Dictionary<string, string> { ["port 1"] = "in", ["port 2"] = "out" });
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            stored.ShouldContainKey("matrix");
            AssertStored(stored["matrix"], ["in", "out"]);
        }
        finally
        {
            finishMapping.TrySetResult(null);
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void AssertStored(ComponentSMatrixData model, string[] portNames)
    {
        model.SourceNote.ShouldBe("Configured parser — original.sparam");
        var entry = model.Wavelengths.ShouldHaveSingleItem();
        entry.Key.ShouldBe("1550");
        entry.Value.PortNames.ShouldBe(portNames);
        entry.Value.Real.ShouldBe(new[] { .1, .3, .5, .7 });
        entry.Value.Imag.ShouldBe(new[] { .2, .4, .6, .8 });
    }
}
