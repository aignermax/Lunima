using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public class GdsCoordinateContractFlowTests
{
    [Theory]
    [InlineData(true, "", "coordinates agree")]
    [InlineData(false, "coordinate mismatch", "STDERR:\ncoordinate mismatch\n\nSTDOUT:\ncoordinates agree")]
    public async Task Comparison_ProjectsContractResultAndTrimsPaths(bool passed, string error, string output)
    {
        var result = new GdsCoordinateComparisonResult
        {
            Passed = passed, MaxDeviationUm = 0.125, RmsDeviationUm = 0.0625,
            ToleranceUm = 0.01, RawOutput = "coordinates agree", ErrorOutput = error
        };
        var comparer = new Mock<IGdsCoordinateComparer>(MockBehavior.Strict);
        comparer.Setup(x => x.CompareAsync("reference.json", "system.json", null,
            It.Is<CancellationToken>(token => token.CanBeCanceled))).ReturnsAsync(result);
        await using var vm = new GdsCoordinateComparisonViewModel(comparer.Object)
        {
            ReferenceJsonPath = " reference.json ", SystemJsonPath = " system.json "
        };

        vm.RunComparisonCommand.CanExecute(null).ShouldBeTrue();
        await vm.RunComparisonCommand.ExecuteAsync(null);

        vm.Passed.ShouldBe(passed);
        vm.HasResults.ShouldBeTrue();
        vm.IsComparing.ShouldBeFalse();
        vm.ComparisonStatus.ShouldBe(result.StatusText);
        vm.ResultText.ShouldBe(output);
        vm.MaxDeviationText.ShouldNotBeEmpty();
        comparer.VerifyAll();
    }

    [Fact]
    public async Task Extraction_UsesSelectedInterpreterAndPublishesDocumentFromContract()
    {
        var extractor = new Mock<IGdsCoordinateExtractor>(MockBehavior.Strict);
        extractor.Setup(x => x.SetCustomPythonPath("selected-python"));
        extractor.Setup(x => x.ExtractAsync("chip.gds", null,
            It.Is<CancellationToken>(token => token.CanBeCanceled))).ReturnsAsync(
            new GdsCoordinateExtractionResult
            {
                Success = true, JsonOutputPath = "chip.coords.json", Status = "extracted coordinates",
                JsonContent = "{\"database_unit\":1e-9,\"cells\":{\"chip\":{\"polygons\":[{}],\"paths\":[]}}}"
            });
        await using var vm = new GdsCoordExtractViewModel(extractor.Object);
        vm.SetCustomPythonPath("selected-python");
        vm.SetGdsFilePath("chip.gds");

        await vm.ExtractCoordinatesCommand.ExecuteAsync(null);

        vm.HasResult.ShouldBeTrue();
        vm.IsExtracting.ShouldBeFalse();
        vm.OutputJsonPath.ShouldBe("chip.coords.json");
        vm.StatusText.ShouldBe("extracted coordinates");
        vm.ResultSummary.ShouldNotBeEmpty();
        extractor.VerifyAll();
    }
}
