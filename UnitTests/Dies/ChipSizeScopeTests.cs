using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Dies;

/// <summary>Root chip settings must not edit an inactive size in die-based designs.</summary>
public class ChipSizeScopeTests
{
    [Fact]
    public void EditingLooseGroupBesideExplicitDie_KeepsRootSizeLocked()
    {
        var preferences = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"chip-size-{Guid.NewGuid()}.json"));
        var canvas = new DesignCanvasViewModel();
        var settings = new ChipSizeViewModel(preferences, canvas);
        var nested = new ComponentGroup("Nested");
        nested.AddChild(DieTestFixtures.Part("part", 200, 200));
        var looseGroup = new ComponentGroup("Loose group");
        looseGroup.AddChild(nested);
        canvas.Components.Add(new ComponentViewModel(looseGroup));
        canvas.Components.Add(new ComponentViewModel(DieTestFixtures.Die("Die", 3000, 0, 1000, 1000)));

        canvas.EnterGroupEditMode(looseGroup);
        settings.ApplyCommand.CanExecute(null).ShouldBeFalse();
        canvas.EnterGroupEditMode(nested);
        settings.ApplyCommand.CanExecute(null).ShouldBeFalse();
        canvas.ExitToRoot();
        settings.ApplyCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void RemovingExplicitDie_ReenablesLegacyApply_AndNotifiesTheButton()
    {
        var preferences = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"chip-size-{Guid.NewGuid()}.json"));
        var canvas = new DesignCanvasViewModel();
        var settings = new ChipSizeViewModel(preferences, canvas);
        var enabledStates = new List<bool>();
        settings.ApplyCommand.CanExecuteChanged += (_, _) => enabledStates.Add(settings.ApplyCommand.CanExecute(null));
        var die = new ComponentViewModel(DieTestFixtures.Die("Die", 3000, 0, 1000, 1000));

        canvas.Components.Add(die);
        canvas.Components.Remove(die);
        settings.WidthMillimeters = 2;
        settings.HeightMillimeters = 3;
        settings.ApplyCommand.Execute(null);

        enabledStates.ShouldContain(false);
        enabledStates.Last().ShouldBeTrue();
        canvas.Dies.RootChipWidthMicrometers.ShouldBe(2000);
        canvas.Dies.RootChipHeightMicrometers.ShouldBe(3000);
        preferences.GetDefaultChipWidthMm().ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitDie_DisablesApply_WithoutChangingRootSizeOrPreferences(bool insideGroup)
    {
        var preferences = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"chip-size-{Guid.NewGuid()}.json"));
        var canvas = new DesignCanvasViewModel();
        var settings = new ChipSizeViewModel(preferences, canvas);
        settings.ApplyFromMicrometers(2000, 2000);
        var originalDefault = preferences.GetDefaultChipWidthMm();
        var group = new ComponentGroup("Nested");
        group.AddChild(DieTestFixtures.Part("part", 3200, 200));
        var die = DieTestFixtures.Die("Die", 3000, 0, 1000, 1000, children: [group]);
        canvas.Components.Add(new ComponentViewModel(die));
        canvas.Dies.ApplyScopeBounds();
        if (insideGroup)
            canvas.EnterGroupEditMode(group);
        var bounds = canvas.Dies.CurrentBounds();

        settings.WidthMillimeters = 3;
        settings.ApplyCommand.CanExecute(null).ShouldBeFalse();
        settings.ApplyCommand.Execute(null);

        canvas.Dies.RootChipWidthMicrometers.ShouldBe(2000);
        preferences.GetDefaultChipWidthMm().ShouldBe(originalDefault);
        canvas.Dies.CurrentBounds().ShouldBe(bounds);
        die.Die!.WidthMicrometers.ShouldBe(1000);
    }

    [Fact]
    public void LoadingRootSize_InAssembly_PreservesDieBounds()
    {
        var preferences = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"chip-size-{Guid.NewGuid()}.json"));
        var canvas = new DesignCanvasViewModel();
        var settings = new ChipSizeViewModel(preferences, canvas);
        canvas.Components.Add(new ComponentViewModel(DieTestFixtures.Die("Die", 3000, 0, 1000, 1000)));
        canvas.Dies.ApplyScopeBounds();
        var bounds = canvas.Dies.CurrentBounds();

        settings.ApplyFromMicrometers(2500, 3000);

        canvas.Dies.RootChipWidthMicrometers.ShouldBe(2500);
        canvas.Dies.RootChipHeightMicrometers.ShouldBe(3000);
        canvas.Dies.CurrentBounds().ShouldBe(bounds);
    }
}
