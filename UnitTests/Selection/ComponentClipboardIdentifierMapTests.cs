using System.Linq;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Selection;

/// <summary>
/// Tests that <see cref="CAP.Avalonia.Selection.PasteResult.IdentifierMap"/> records the
/// old→new identifier mapping for pasted components, which downstream code uses to carry
/// identifier-keyed state (Nazca overrides) onto the copies.
/// </summary>
public class ComponentClipboardIdentifierMapTests
{
    [Fact]
    public void Paste_RegularComponent_MapsOriginalIdentifierToCopyIdentifier()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.Identifier = "MMI_1x2";
        var vm = canvas.AddComponent(comp);

        canvas.Clipboard.Copy(new[] { vm }, canvas.Connections);
        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        cmd.Result.ShouldNotBeNull();
        var copyId = cmd.Result!.Components[0].Component.Identifier;
        cmd.Result.IdentifierMap.ContainsKey("MMI_1x2").ShouldBeTrue();
        cmd.Result.IdentifierMap["MMI_1x2"].ShouldBe(copyId);
        copyId.ShouldBe("MMI_1x2_1");
    }

    [Fact]
    public void Paste_ComponentGroup_MapsGroupAndEveryChildIdentifier()
    {
        var canvas = new DesignCanvasViewModel();
        var group = TestComponentFactory.CreateComponentGroup("Circuit", addChildren: true);
        var oldGroupId = group.Identifier;
        var oldChildIds = group.ChildComponents.Select(c => c.Identifier).ToList();

        var groupVm = canvas.AddComponent(group);
        canvas.Clipboard.Copy(new[] { groupVm }, canvas.Connections);
        var cmd = new PasteComponentsCommand(canvas, canvas.Clipboard);
        cmd.Execute();

        cmd.Result.ShouldNotBeNull();
        var map = cmd.Result!.IdentifierMap;

        map.ContainsKey(oldGroupId).ShouldBeTrue();
        var pastedGroup = (ComponentGroup)cmd.Result.Components[0].Component;
        map[oldGroupId].ShouldBe(pastedGroup.Identifier);

        foreach (var oldChildId in oldChildIds)
            map.ContainsKey(oldChildId).ShouldBeTrue($"Child {oldChildId} should be in the identifier map.");
    }
}
