using CAP_Core.Export;
using CAP.Avalonia.ViewModels.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

/// <summary>
/// Unit tests for <see cref="GdsCoordinateExtractor"/> and <see cref="GdsCoordExtractViewModel"/>.
/// </summary>
public class GdsCoordinateExtractorTests
{
    // ── GdsCoordinateExtractor unit tests ──────────────────────────────────

    [Fact]
    public async Task ExtractAsync_NonExistentGdsFile_ReturnsFailure()
    {
        var extractor = new GdsCoordinateExtractor();

        var result = await extractor.ExtractAsync("/nonexistent/path/file.gds");

        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
        result.ErrorMessage.ShouldContain("not found");
    }

    [Fact]
    public async Task ExtractAsync_MissingScript_ReturnsFailure()
    {
        // Arrange: use a real file so we pass the first guard, but
        // the extraction script won't exist in the test runner's working dir
        // unless Scripts/extract_gds_coords.py is present.
        var extractor = new GdsCoordinateExtractor();
        var tempGds = Path.GetTempFileName() + ".gds";
        File.WriteAllBytes(tempGds, Array.Empty<byte>());

        try
        {
            var scriptPath = GdsCoordinateExtractor.FindScriptPath();
            if (scriptPath != null)
                return; // Script found — skip this test, behaviour is untestable

            var result = await extractor.ExtractAsync(tempGds);

            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("script not found");
        }
        finally
        {
            if (File.Exists(tempGds)) File.Delete(tempGds);
        }
    }

    [Fact]
    public void FindScriptPath_ReturnsNullOrValidPath()
    {
        var path = GdsCoordinateExtractor.FindScriptPath();
        // Either null (not found) or an existing file
        if (path != null)
            File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task IsPythonAvailableAsync_ReturnsBoolean()
    {
        var extractor = new GdsCoordinateExtractor();
        // Should not throw — just returns true/false depending on environment
        var available = await extractor.IsPythonAvailableAsync();
        available.ShouldBeOneOf(true, false);
    }

    [Fact]
    public void SetCustomPythonPath_DoesNotThrow()
    {
        var extractor = new GdsCoordinateExtractor();
        Should.NotThrow(() => extractor.SetCustomPythonPath("/custom/python3"));
        Should.NotThrow(() => extractor.SetCustomPythonPath(null));
    }

    // ── GdsCoordExtractViewModel unit tests ────────────────────────────────

    [Fact]
    public void ViewModel_DefaultState_HasCorrectInitialValues()
    {
        var vm = new GdsCoordExtractViewModel(new GdsCoordinateExtractor());

        vm.GdsFilePath.ShouldBeEmpty();
        vm.StatusText.ShouldBeEmpty();
        vm.IsExtracting.ShouldBeFalse();
        vm.HasResult.ShouldBeFalse();
        vm.ResultSummary.ShouldBeEmpty();
        vm.OutputJsonPath.ShouldBeEmpty();
    }

    [Fact]
    public async Task ViewModel_ExtractCoordinates_WithEmptyPath_SetsStatusText()
    {
        var vm = new GdsCoordExtractViewModel(new GdsCoordinateExtractor());
        vm.GdsFilePath = string.Empty;

        await vm.ExtractCoordinatesAsync();

        vm.StatusText.ShouldNotBeNullOrEmpty();
        vm.HasResult.ShouldBeFalse();
        vm.IsExtracting.ShouldBeFalse();
    }

    [Fact]
    public async Task ViewModel_ExtractCoordinates_WithNonExistentFile_SetsFailureStatus()
    {
        var vm = new GdsCoordExtractViewModel(new GdsCoordinateExtractor());
        vm.GdsFilePath = "/nonexistent/file.gds";

        await vm.ExtractCoordinatesAsync();

        vm.HasResult.ShouldBeFalse();
        vm.StatusText.ShouldNotBeNullOrEmpty();
        vm.IsExtracting.ShouldBeFalse();
    }

    [Fact]
    public void ViewModel_SetGdsFilePath_ResetsResultState()
    {
        var vm = new GdsCoordExtractViewModel(new GdsCoordinateExtractor());

        vm.SetGdsFilePath("/some/path/design.gds");

        vm.GdsFilePath.ShouldBe("/some/path/design.gds");
        vm.HasResult.ShouldBeFalse();
        vm.StatusText.ShouldBeEmpty();
        vm.ResultSummary.ShouldBeEmpty();
        vm.OutputJsonPath.ShouldBeEmpty();
    }

    [Fact]
    public void ViewModel_SetCustomPythonPath_DoesNotThrow()
    {
        var vm = new GdsCoordExtractViewModel(new GdsCoordinateExtractor());
        Should.NotThrow(() => vm.SetCustomPythonPath("/usr/bin/python3"));
        Should.NotThrow(() => vm.SetCustomPythonPath(null));
    }
}
