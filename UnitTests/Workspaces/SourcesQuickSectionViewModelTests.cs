using System.Collections.ObjectModel;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Workspaces.ContextPanels;
using Shouldly;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// The Optics "Sources &amp; detectors" quick section mirrors the library's filtered list,
/// keeping only where light enters or leaves (fiber couplers, lasers, detectors, terminators).
/// </summary>
public class SourcesQuickSectionViewModelTests
{
    private static ComponentTemplate T(string name) => new() { Name = name };

    [Theory]
    [InlineData("Grating Coupler TE 1550", true)]
    [InlineData("Edge Coupler", true)]
    [InlineData("Photodetector", true)]
    [InlineData("Terminator", true)]
    [InlineData("MMI 2x2", false)]
    [InlineData("Directional Coupler", false)]
    [InlineData("Straight Waveguide", false)]
    public void IsSourceOrDetector_ClassifiesByName(string name, bool expected)
    {
        SourcesQuickSectionViewModel.IsSourceOrDetector(T(name)).ShouldBe(expected);
    }

    [Fact]
    public void Items_FollowTheFilteredLibrary_ThroughClearAndAdd()
    {
        var filtered = new ObservableCollection<ComponentTemplate> { T("Grating Coupler TE 1550"), T("MMI 2x2") };
        var section = new SourcesQuickSectionViewModel(filtered);
        section.Items.Select(t => t.Name).ShouldBe(new[] { "Grating Coupler TE 1550" });
        section.HasItems.ShouldBeTrue();

        filtered.Clear();
        section.HasItems.ShouldBeFalse();

        filtered.Add(T("Straight Waveguide"));
        filtered.Add(T("Photodetector"));
        filtered.Add(T("Edge Coupler"));
        section.Items.Select(t => t.Name).ShouldBe(new[] { "Photodetector", "Edge Coupler" });

        filtered.RemoveAt(1);
        section.Items.Select(t => t.Name).ShouldBe(new[] { "Edge Coupler" });

        filtered.Insert(0, T("Terminator"));
        section.Items.Select(t => t.Name).ShouldBe(new[] { "Terminator", "Edge Coupler" }, "inserts keep library order");
    }
}
