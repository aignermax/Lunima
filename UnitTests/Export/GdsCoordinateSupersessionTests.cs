using System.ComponentModel;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public sealed class GdsCoordinateSupersessionTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(8);

    [Theory]
    [InlineData("extraction-path")]
    [InlineData("comparison-path")]
    [InlineData("comparison-clear")]
    public async Task InputOrResetChange_RejectsTheHeldPreviousResult(string change)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task running;
        Func<Task> stop;
        Action supersede;
        Func<bool> hasResult;
        Func<string> displayed;

        if (change == "extraction-path")
        {
            var port = new Mock<IGdsCoordinateExtractor>(MockBehavior.Strict);
            port.Setup(service => service.ExtractAsync("old.gds", null, It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    started.TrySetResult();
                    await release.Task;
                    return new GdsCoordinateExtractionResult
                    {
                        Success = true, JsonOutputPath = "old-output.json", JsonContent = "{}", Status = "old result"
                    };
                });
            var vm = new GdsCoordExtractViewModel(port.Object) { GdsFilePath = "old.gds" };
            running = vm.ExtractCoordinatesCommand.ExecuteAsync(null);
            stop = vm.StopAsync;
            supersede = () => vm.SetGdsFilePath("replacement.gds");
            hasResult = () => vm.HasResult;
            displayed = () => vm.OutputJsonPath;
        }
        else
        {
            var port = new Mock<IGdsCoordinateComparer>(MockBehavior.Strict);
            port.Setup(service => service.CompareAsync("old-reference.json", "old-system.json", null,
                    It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    started.TrySetResult();
                    await release.Task;
                    return new GdsCoordinateComparisonResult { Passed = true, RawOutput = "old result" };
                });
            var vm = new GdsCoordinateComparisonViewModel(port.Object)
                { ReferenceJsonPath = "old-reference.json", SystemJsonPath = "old-system.json" };
            running = vm.RunComparisonCommand.ExecuteAsync(null);
            stop = vm.StopAsync;
            supersede = change == "comparison-clear"
                ? vm.ClearResults : () => vm.SystemJsonPath = "replacement.json";
            hasResult = () => vm.HasResults;
            displayed = () => vm.ResultText;
        }

        try
        {
            await started.Task.WaitAsync(Guard);
            supersede();
            release.TrySetResult();
            await running.WaitAsync(Guard);
            hasResult().ShouldBeFalse("the accepted old request cannot republish after its inputs or result presentation were replaced");
            displayed().ShouldBeEmpty();
        }
        finally
        {
            release.TrySetResult();
            await running.WaitAsync(Guard);
            await stop().WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task Extraction_ResetObserverRetiresPanel_BeforeRemainingResetFieldsChange()
    {
        await using var vm = new GdsCoordExtractViewModel(Mock.Of<IGdsCoordinateExtractor>())
        {
            GdsFilePath = "old.gds", HasResult = true, StatusText = "keep-status",
            ResultSummary = "keep-summary", OutputJsonPath = "keep-output"
        };
        Task? stopping = null;
        vm.PropertyChanged += RetireOnPath;
        try
        {
            vm.SetGdsFilePath("replacement.gds");
            stopping.ShouldNotBeNull();
            await stopping!.WaitAsync(Guard);
            vm.HasResult.ShouldBeTrue();
            vm.StatusText.ShouldBe("keep-status");
            vm.ResultSummary.ShouldBe("keep-summary");
            vm.OutputJsonPath.ShouldBe("keep-output");
        }
        finally { vm.PropertyChanged -= RetireOnPath; }

        void RetireOnPath(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.GdsFilePath)) stopping = vm.StopAsync();
        }
    }

    [Fact]
    public async Task Comparison_ClearObserverRetiresPanel_BeforeRemainingResetFieldsChange()
    {
        await using var vm = new GdsCoordinateComparisonViewModel(Mock.Of<IGdsCoordinateComparer>())
        {
            HasResults = true, Passed = true, ResultText = "keep-result",
            MaxDeviationText = "keep-deviation", ComparisonStatus = "keep-status"
        };
        Task? stopping = null;
        vm.PropertyChanged += RetireOnReset;
        try
        {
            vm.ClearResults();
            stopping.ShouldNotBeNull();
            await stopping!.WaitAsync(Guard);
            vm.Passed.ShouldBeTrue();
            vm.ResultText.ShouldBe("keep-result");
            vm.MaxDeviationText.ShouldBe("keep-deviation");
            vm.ComparisonStatus.ShouldBe("keep-status");
        }
        finally { vm.PropertyChanged -= RetireOnReset; }

        void RetireOnReset(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(vm.HasResults)) stopping = vm.StopAsync();
        }
    }
}
