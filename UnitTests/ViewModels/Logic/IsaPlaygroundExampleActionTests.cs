using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using Shouldly;

namespace UnitTests.ViewModels.Logic;

public class IsaPlaygroundExampleActionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenExample_OnlyOffersBuildAfterSuccessfulOpen(bool opened)
    {
        var vm = new IsaPlaygroundViewModel(new CAP.Avalonia.Services.ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
        var examples = new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()).GetExamples();
        var expected = examples.Single(e => Path.GetFileName(e.FilePath) == "Logic Gate ALU + RAM + ACC.lun");
        string? path = null;
        int builds = 0;
        vm.OpenExampleRequested = p => { path = p; return Task.FromResult(opened); };
        vm.BuildLogicNetworkRequested = () => { builds++; return Task.FromResult(true); };
        await vm.OpenSuitableExampleCommand.ExecuteAsync(null);
        path.ShouldBe(expected.FilePath);
        vm.HasOpenedSuitableExample.ShouldBe(opened);
        vm.BuildExampleNetworkCommand.CanExecute(null).ShouldBe(opened);
        builds.ShouldBe(0, "opening must not silently build");
        if (opened)
        {
            await vm.BuildExampleNetworkCommand.ExecuteAsync(null);
            builds.ShouldBe(1);
            vm.HasOpenedSuitableExample.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task MissingCombinedExample_FallsBackToShippedAdder_AndBuildFailureCanRetry()
    {
        var adder = new ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()).GetExamples().Single(e => Path.GetFileName(e.FilePath) == "Logic Gate 4-Bit Adder.lun");
        var vm = new IsaPlaygroundViewModel(new ExampleDesignsService(new FileExampleCatalog())) { FindExamples = () => new[] { adder } };
        vm.OpenExampleRequested = p => { p.ShouldBe(adder.FilePath); return Task.FromResult(true); };
        vm.BuildLogicNetworkRequested = () => Task.FromResult(false);
        await vm.OpenSuitableExampleCommand.ExecuteAsync(null);
        await vm.BuildExampleNetworkCommand.ExecuteAsync(null);
        vm.HasOpenedSuitableExample.ShouldBeTrue();
        vm.ExampleActionStatus.ShouldNotBeNullOrEmpty();
        vm.BuildExampleNetworkCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task MissingExamples_DoesNotOpenOrOfferBuild()
    {
        var vm = new IsaPlaygroundViewModel(new ExampleDesignsService(new FileExampleCatalog())) { FindExamples = () => Array.Empty<ExampleDesign>() };
        vm.OpenExampleRequested = _ => throw new InvalidOperationException("must not open");
        await vm.OpenSuitableExampleCommand.ExecuteAsync(null);
        vm.HasOpenedSuitableExample.ShouldBeFalse();
        vm.ExampleActionStatus.ShouldNotBeNullOrEmpty();
    }
}
