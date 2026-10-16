using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.Views.Panels;
using UnitTests.Integration;
using UnitTests.Integration.RamScale;
using Moq;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class StoreNumberLearningJourneyTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task ActualControls_StoreAndReadBack(string language)
    {
        using var host = new UiFlowTestHost();
        var window = host.Window;
        var vm = host.Vm;
        window.Width = 1280; window.Height = 800;
        LocalizationService.Instance.SetLanguage(language);
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(vm.Canvas);
        Assert.True(await fileOps.LoadDesignFromPathAsync(Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), StoreNumberTourViewModel.RamExampleFileName)));
        await fileOps.PostLoadRouting;
        var tour = vm.StoreNumberTour;
        var logic = vm.RightPanel.Logic;
        tour.Start();
        await Settle();
        var overlay = UiInput.Descendants<TourAnchorOverlay>(window).Single(o => o.Card is StoreNumberTourPanel);
        Assert.True(overlay.Demonstrate);
        Save("build");
        Click("LogicBuildButton");
        for (var i = 0; i < 100 && !logic.HasNetwork; i++) { await Task.Delay(20); UiInput.RunJobs(); }
        Assert.True(logic.HasNetwork, $"Build status: {logic.StatusText}; parts: {vm.Canvas.Components.Count}; workspace: {vm.WorkspaceSwitch.Current}");
        await Toggle("D0");
        await Toggle("LOAD");
        Assert.Equal("LogicStepClockButton", tour.CurrentTargetName);
        Click("LogicStepClockButton");
        Assert.Equal(3, tour.CurrentStepIndex);
        Assert.True(logic.Outputs.Single(o => o.PinName == "Q0").IsOne);
        await Toggle("LOAD");
        await Toggle("A");
        Assert.Equal(3, tour.CurrentStepIndex);
        await Toggle("A");
        Assert.Equal(4, tour.CurrentStepIndex);
        Assert.True(logic.Outputs.Single(o => o.PinName == "Q0").IsOne);
        Save("readback");
        Click("StoreTourNextButton");
        Assert.True(tour.IsCompleted);

        async Task Toggle(string name)
        {
            await Settle();
            var item = logic.Inputs.Single(i => i.PinName == name);
            Assert.Same(item, overlay.TargetItem);
            var check = UiInput.Descendants<CheckBox>(window).Single(c => ReferenceEquals(c.DataContext, item));
            Assert.True(check.IsEffectivelyVisible);
            Save(name);
            UiInput.Click(window, check);
            await Settle();
        }
        void Click(string name)
        {
            var button = UiInput.Descendants<Button>(window).Single(b => b.Name == name && b.IsEffectivelyVisible);
            Assert.True(button.IsEnabled);
            UiInput.Click(window, button);
        }
        async Task Settle() { await Task.Delay(180); UiInput.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); UiInput.RunJobs(); }
        void Save(string step)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "store-number");
            Directory.CreateDirectory(dir);
            using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 800));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(dir, $"{language}-{step}.png"));
        }
    }
    [AvaloniaFact]
    public async Task CancelExampleOpenPreservesUnsavedDesign()
    {
        using var host = new UiFlowTestHost();
        var part = host.Vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide());
        var prompt = new Moq.Mock<CAP.Avalonia.Services.IMessageBoxService>();
        prompt.Setup(p => p.ShowSavePromptAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()))
            .ReturnsAsync(CAP.Avalonia.Services.SavePromptResult.Cancel);
        host.Vm.FileOperations.MessageBoxService = prompt.Object;
        await host.Vm.Home.StoreNumberTourCommand.ExecuteAsync(null);
        Assert.Contains(part, host.Vm.Canvas.Components);
        Assert.True(host.Vm.FileOperations.HasUnsavedChanges);
        Assert.False(host.Vm.StoreNumberTour.IsActive);
    }

}
