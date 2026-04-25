using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.ComponentSettings;

/// <summary>
/// Tests for <see cref="ComponentSettingsDialogViewModel"/>.
/// Verifies S-matrix entry display, deletion, and configuration behaviour.
/// </summary>
public class ComponentSettingsDialogViewModelTests
{
    private static Dictionary<string, ComponentSMatrixData> MakeStore(
        string key,
        params string[] wavelengthNms)
    {
        var data = new ComponentSMatrixData { SourceNote = "Test" };

        foreach (var wl in wavelengthNms)
        {
            data.Wavelengths[wl] = new SMatrixWavelengthEntry
            {
                Rows = 2,
                Cols = 2,
                Real = new List<double> { 0.9, 0.1, 0.1, 0.9 },
                Imag = new List<double> { 0, 0, 0, 0 },
                PortNames = new List<string> { "port1", "port2" }
            };
        }

        return new Dictionary<string, ComponentSMatrixData> { [key] = data };
    }

    [Fact]
    public void Configure_WithExistingEntries_PopulatesSMatrixEntries()
    {
        var store = MakeStore("comp_1", "1550", "1310");
        var vm = new ComponentSettingsDialogViewModel();

        vm.Configure("comp_1", "My Component", store);

        vm.SMatrixEntries.Count.ShouldBe(2);
        vm.HasSMatrices.ShouldBeTrue();
        vm.Title.ShouldContain("My Component");
    }

    [Fact]
    public void Configure_WithNoMatchingKey_EmptyEntries()
    {
        var store = MakeStore("comp_1", "1550");
        var vm = new ComponentSettingsDialogViewModel();

        vm.Configure("comp_99", "Unknown", store);

        vm.SMatrixEntries.Count.ShouldBe(0);
        vm.HasSMatrices.ShouldBeFalse();
    }

    [Fact]
    public void DeleteEntryCommand_RemovesWavelength_RefreshesEntries()
    {
        var store = MakeStore("comp_1", "1550", "1310");
        var vm = new ComponentSettingsDialogViewModel();
        vm.Configure("comp_1", "My Component", store);

        vm.SMatrixEntries.Count.ShouldBe(2);
        var entryToDelete = vm.SMatrixEntries.First();

        vm.DeleteEntryCommand.Execute(entryToDelete);

        vm.SMatrixEntries.Count.ShouldBe(1);
        vm.HasSMatrices.ShouldBeTrue();
        vm.StatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void DeleteEntryCommand_LastEntry_RemovesKeyFromStore()
    {
        var store = MakeStore("comp_1", "1550");
        var vm = new ComponentSettingsDialogViewModel();
        vm.Configure("comp_1", "My Component", store);

        vm.DeleteEntryCommand.Execute(vm.SMatrixEntries[0]);

        store.ContainsKey("comp_1").ShouldBeFalse();
        vm.HasSMatrices.ShouldBeFalse();
    }

    [Fact]
    public void Configure_RecalledTwice_ReflectsLatestStore()
    {
        var store = MakeStore("comp_1", "1550");
        var vm = new ComponentSettingsDialogViewModel();
        vm.Configure("comp_1", "My Component", store);

        // Add another wavelength externally
        store["comp_1"].Wavelengths["980"] = new SMatrixWavelengthEntry
        {
            Rows = 2, Cols = 2,
            Real = new List<double> { 0.8, 0.2, 0.2, 0.8 },
            Imag = new List<double> { 0, 0, 0, 0 }
        };

        vm.Configure("comp_1", "My Component", store);

        vm.SMatrixEntries.Count.ShouldBe(2);
    }

    [Fact]
    public void SMatrixEntryViewModel_DiagonalPreview_ShowsMagnitudes()
    {
        var entry = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            Real = new List<double> { 0.9, 0.1, 0.1, 0.9 },
            Imag = new List<double> { 0, 0, 0, 0 },
            PortNames = new List<string> { "port1", "port2" }
        };

        var vm = new SMatrixEntryViewModel("1550", entry, "TestSource");

        vm.WavelengthLabel.ShouldBe("1550 nm");
        vm.Dimensions.ShouldBe("2 × 2");
        vm.PortNamesDisplay.ShouldBe("port1, port2");
        vm.DiagonalPreview.ShouldContain("|S11|=");
        vm.SourceNote.ShouldBe("TestSource");
    }

    [Fact]
    public void SMatrixEntryViewModel_NoPortNames_ShowsFallbackText()
    {
        var entry = new SMatrixWavelengthEntry
        {
            Rows = 2,
            Cols = 2,
            Real = new List<double> { 1, 0, 0, 1 },
            Imag = new List<double> { 0, 0, 0, 0 }
        };

        var vm = new SMatrixEntryViewModel("1310", entry, null);

        vm.PortNamesDisplay.ShouldBe("(no port names)");
        vm.SourceNote.ShouldBeNull();
    }
}
