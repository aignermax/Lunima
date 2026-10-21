using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;
using CAP_DataAccess.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public sealed class GdsCombinedRouteLayerWarningTests
{
    [Theory]
    [InlineData(GdsLayerRole.OpticalCore, GdsLayerRole.OpticalCore, true)]
    [InlineData(GdsLayerRole.Electrical, GdsLayerRole.Electrical, true)]
    [InlineData(GdsLayerRole.Electrical, GdsLayerRole.Heater, true)]
    [InlineData(GdsLayerRole.OpticalCore, GdsLayerRole.Electrical, false)]
    [InlineData(GdsLayerRole.OpticalCore, GdsLayerRole.Slab, false)]
    [InlineData(GdsLayerRole.Unknown, GdsLayerRole.Unknown, false)]
    public async Task WarningCountsEachRoutingDomainSeparately(
        GdsLayerRole first, GdsLayerRole second, bool expected)
    {
        var vm = await Create(first, second);
        try
        {
            Assert.Equal(expected, vm.HasCombinedRouteLayers);
            Assert.Equal(expected, !string.IsNullOrWhiteSpace(vm.CombinedRouteLayerWarning));
            for (var i = 0; i < 5; i++) await vm.NextCommand.ExecuteAsync(null);
            Assert.True(vm.CanApply); // Warning must not forbid valid multilayer designs.
        }
        finally { vm.Close(); }
    }

    [Fact]
    public async Task RoleChangeAndUndoNotifyWarningVisibilityAndText()
    {
        var vm = await Create(GdsLayerRole.OpticalCore, GdsLayerRole.Slab);
        try
        {
            var changes = new List<string?>();
            vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            vm.Layers.Single(x => x.Layer == 2).Role = GdsLayerRole.OpticalCore;
            Assert.True(vm.HasCombinedRouteLayers);
            Assert.Contains(nameof(vm.HasCombinedRouteLayers), changes);
            Assert.Contains(nameof(vm.CombinedRouteLayerWarning), changes);
            changes.Clear();
            vm.UndoCommand.Execute(null);
            Assert.False(vm.HasCombinedRouteLayers);
            Assert.Empty(vm.CombinedRouteLayerWarning);
            Assert.Contains(nameof(vm.HasCombinedRouteLayers), changes);
            Assert.Contains(nameof(vm.CombinedRouteLayerWarning), changes);
        }
        finally { vm.Close(); }
    }

    private static Task<GdsInterpretationReviewViewModel> Create(GdsLayerRole first, GdsLayerRole second)
    {
        var library = new GdsLibrary();
        var cell = new GdsCell { Name = "TOP" };
        foreach (var layer in new[] { 1, 2 })
            cell.Elements.Add(new GdsPolygon
            {
                Layer = layer,
                Points = [new(0, 0), new(10, 0), new(10, 5), new(0, 5)]
            });
        library.Cells.Add(cell.Name, cell);
        return GdsInterpretationReviewViewModel.CreateAsync(library, cell.Name, "test",
            [new(1, 0, first), new(2, 0, second)], [],
            new GdsLayerProfileStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "profiles.json")));
    }
}
