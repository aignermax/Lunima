using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Tests for the HumanReadableName feature: display name separate from Identifier GUID.
/// </summary>
public class HumanReadableNameTests
{
    // ─── Component property ───────────────────────────────────────────────────

    [Fact]
    public void Component_HumanReadableName_DefaultsToNull()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName.ShouldBeNull();
    }

    [Fact]
    public void Component_HumanReadableName_CanBeSet()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "WaveGuide_1";
        comp.HumanReadableName.ShouldBe("WaveGuide_1");
    }

    [Fact]
    public void Component_Identifier_RemainsUnchangedWhenHumanReadableNameIsSet()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var originalId = comp.Identifier;

        comp.HumanReadableName = "WaveGuide_1";

        comp.Identifier.ShouldBe(originalId);
    }

    [Fact]
    public void Component_Clone_CopiesHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "WaveGuide_1";

        var clone = (Component)comp.Clone();

        clone.HumanReadableName.ShouldBe("WaveGuide_1");
    }

    [Fact]
    public void Component_Clone_PreservesNullHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();

        var clone = (Component)comp.Clone();

        clone.HumanReadableName.ShouldBeNull();
    }

    // ─── GroupLibraryManager ─────────────────────────────────────────────────

    [Fact]
    public void GroupLibraryManager_InstantiateTemplate_SetsHumanReadableNameOnChildren()
    {
        var manager = new GroupLibraryManager(Path.GetTempPath());
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp1);
        group.AddChild(comp2);

        var template = manager.SaveTemplate(group, "TestTemplate");
        var instance = manager.InstantiateTemplate(template, 0, 0);

        instance.ChildComponents[0].HumanReadableName.ShouldNotBeNullOrWhiteSpace();
        instance.ChildComponents[1].HumanReadableName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GroupLibraryManager_InstantiateTemplate_IdentifierIsDifferentFromHumanReadableName()
    {
        var manager = new GroupLibraryManager(Path.GetTempPath());
        var comp = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp);

        var template = manager.SaveTemplate(group, "TestTemplate");
        var instance = manager.InstantiateTemplate(template, 0, 0);

        var child = instance.ChildComponents[0];
        // HumanReadableName is set for display; Identifier is a separate unique value for persistence
        child.HumanReadableName.ShouldNotBeNullOrWhiteSpace();
        child.Identifier.ShouldNotBe(child.HumanReadableName);
    }

    [Fact]
    public void GroupLibraryManager_InstantiateTemplate_NamesContainNazcaFunctionName()
    {
        var manager = new GroupLibraryManager(Path.GetTempPath());
        var comp = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp);

        var template = manager.SaveTemplate(group, "TestTemplate");
        var instance = manager.InstantiateTemplate(template, 0, 0);

        // NazcaFunctionName is "placeCell_StraightWG"
        instance.ChildComponents[0].HumanReadableName.ShouldContain("placeCell_StraightWG");
    }

    // ─── ComponentViewModel ───────────────────────────────────────────────────

    [Fact]
    public void ComponentViewModel_Name_FallsBackToIdentifierWhenHumanReadableNameIsNull()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var vm = new ComponentViewModel(comp);

        vm.Name.ShouldBe(comp.Identifier);
    }

    [Fact]
    public void ComponentViewModel_Name_ShowsHumanReadableNameWhenSet()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "StraightWG_1";
        var vm = new ComponentViewModel(comp);

        vm.Name.ShouldBe("StraightWG_1");
    }

    // ─── HierarchyNodeViewModel ───────────────────────────────────────────────

    [Fact]
    public void HierarchyNode_DisplayName_FallsBackToIdentifierWhenHumanReadableNameIsNull()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var node = new HierarchyNodeViewModel(comp);

        node.DisplayName.ShouldBe(comp.Identifier);
    }

    [Fact]
    public void HierarchyNode_DisplayName_ShowsHumanReadableNameWhenSet()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "StraightWG_1";
        var node = new HierarchyNodeViewModel(comp);

        node.DisplayName.ShouldBe("StraightWG_1");
    }

    // ─── Persistence (serialize / deserialize) ───────────────────────────────

    [Fact]
    public void GroupTemplateSerializer_RoundTrip_PreservesHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "StraightWG_1";

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp);

        var json = GroupTemplateSerializer.Serialize(group);
        var restored = GroupTemplateSerializer.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.ChildComponents[0].HumanReadableName.ShouldBe("StraightWG_1");
    }

    [Fact]
    public void GroupTemplateSerializer_RoundTrip_PreservesNullHumanReadableName()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp);

        var json = GroupTemplateSerializer.Serialize(group);
        var restored = GroupTemplateSerializer.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.ChildComponents[0].HumanReadableName.ShouldBeNull();
    }

    [Fact]
    public void GroupTemplateSerializer_RoundTrip_IdentifierRemainsStable()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.HumanReadableName = "StraightWG_1";
        var originalId = comp.Identifier;

        var group = new ComponentGroup("TestGroup");
        group.AddChild(comp);

        var json = GroupTemplateSerializer.Serialize(group);
        var restored = GroupTemplateSerializer.Deserialize(json);

        restored!.ChildComponents[0].Identifier.ShouldBe(originalId);
    }
}
