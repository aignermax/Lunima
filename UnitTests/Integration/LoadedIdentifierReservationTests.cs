using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Components created after a design was loaded — placed by hand or by the crossing pass —
/// must never reuse the identifier of a loaded component: saved connections reference
/// components by identifier, so a collision silently rewires the design on the next load.
/// </summary>
public class LoadedIdentifierReservationTests
{
    [Fact]
    public async Task CrossingCreatedAfterLoad_GetsAnIdentifierTheDesignDoesNotUse()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample("Logic Gate PC 2-bit.lun");
        await fileOps.PostLoadRouting;
        var inUse = canvas.Components.Select(c => c.Component)
            .SelectMany(c => c is ComponentGroup g ? g.GetAllComponentsRecursive().Prepend(c) : new[] { c })
            .Select(c => c.Identifier)
            .ToHashSet();
        var templates = TestPdkLoader.LoadAllTemplates();

        for (int i = 0; i < 50; i++)
        {
            var crossing = CrossingComponentInstance.CreateFromTemplates(templates, new[] { "Demo PDK" })!.Component;
            inUse.Add(crossing.Identifier).ShouldBeTrue($"'{crossing.Identifier}' already names a component of the loaded design");
        }
    }

    [Theory]
    [InlineData("Waveguide Crossing_3765")]
    [InlineData("2x2 MMI Coupler_120")]
    public void ReserveIdentifier_MovesTheCounterPastTheSuffix(string identifier)
    {
        ComponentTemplates.ReserveIdentifier(identifier);
        var template = TestPdkLoader.LoadAllTemplates().First(t => CrossingComponentInstance.IsCrossingTemplate(t));

        var created = ComponentTemplates.CreateFromTemplate(template, 0, 0);

        int suffix = int.Parse(created.Identifier[(created.Identifier.LastIndexOf('_') + 1)..]);
        suffix.ShouldBeGreaterThan(int.Parse(identifier[(identifier.LastIndexOf('_') + 1)..]));
    }
}
