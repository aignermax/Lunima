using Avalonia.Headless.XUnit;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence.NativeDesign;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class NativeDocumentScopeMetadataTests
{
    [AvaloniaFact]
    public async Task SaveInsideGroup_PreservesRootInstanceTemplateIdentity()
    {
        await using var main = MainViewModelTestHelper.CreateMainViewModel();
        var outside = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        outside.Identifier = "outside instance";
        main.Canvas.AddComponent(outside, "explicit instance template", "local instance PDK");
        var group = new ComponentGroup("editable group");
        group.AddChild(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        main.Canvas.AddComponent(group);
        main.Canvas.EnterGroupEditMode(group);
        var path = TemporaryPath();
        try
        {
            (await main.FileOperations.SaveDesignToPathAsync(path)).ShouldBeTrue();
            var data = NativeDesignCodec.Parse(await File.ReadAllTextAsync(path))!;
            var saved = data.Components.Single(item => item.Identifier == outside.Identifier);
            saved.TemplateName.ShouldBe("explicit instance template");
            saved.PdkSource.ShouldBe("local instance PDK");
            main.Canvas.CurrentEditGroup.ShouldBeSameAs(group);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_KeepsReachableModelsAtEveryDepthRegardlessOfVisibleScope(bool insideGroup)
    {
        await using var main = MainViewModelTestHelper.CreateMainViewModel();
        var outside = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        outside.Identifier = "outside instance";
        main.Canvas.AddComponent(outside);
        var nested = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        nested.Identifier = "nested instance";
        var inner = new ComponentGroup("inner") { Identifier = "inner instance" };
        inner.AddChild(nested);
        var parent = new ComponentGroup("parent");
        parent.AddChild(inner);
        main.Canvas.AddComponent(parent);
        if (insideGroup) main.Canvas.EnterGroupEditMode(inner);
        foreach (var key in new[] { outside.Identifier, nested.Identifier, inner.Identifier, "orphan" })
            main.FileOperations.StoredSMatrices[key] = new ComponentSMatrixData { SourceNote = key };
        var path = TemporaryPath();
        try
        {
            (await main.FileOperations.SaveDesignToPathAsync(path)).ShouldBeTrue();
            var data = NativeDesignCodec.Parse(await File.ReadAllTextAsync(path))!;
            data.SMatrices.ShouldNotBeNull();
            data.SMatrices.Keys.ShouldBe(new[] { outside.Identifier, nested.Identifier, inner.Identifier }, ignoreOrder: true);
            main.Canvas.CurrentEditGroup.ShouldBeSameAs(insideGroup ? inner : null);
        }
        finally { File.Delete(path); }
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "document-models-" + Guid.NewGuid() + ".lun");
}
