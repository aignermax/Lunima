using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using UnitTests.Helpers;
using UnitTests.Integration;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class IsaVisualJourneyTests : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;
    public IsaVisualJourneyTests(LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) => _fixture = fixture;

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task PublishedAdder_UserOpensChoosesStepsAndFinishesThroughActualControls(string language)
    {
        var previousServices = App.Services;
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(language);
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var playground = new IsaPlaygroundViewModel(new CAP.Avalonia.Services.ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()),provider);
        var vm = MainViewModelTestHelper.CreateMainViewModel(logicNetworkProvider: provider, isaPlayground: playground);
        using var services = new ServiceCollection().AddSingleton(playground).AddSingleton(vm.RunProgramTour).BuildServiceProvider();
        App.OverrideServicesForTesting(services);
        var main = CAP.Avalonia.DI.MainWindowComposition.Create(services, vm);
        main.Width = 1280;
        main.Height = 900;
        try
        {
            main.Show();
            vm.Home.IsHomeVisible = false;
            vm.RightPanel.Logic.HasNetwork = true;
            vm.RunProgramTour.Start();
            Pump();
            Assert.Equal(1, vm.RunProgramTour.CurrentStepIndex);
            await Open();
            var window = Assert.Single(main.OwnedWindows.OfType<IsaPlaygroundWindow>());
            Assert.Equal(2, vm.RunProgramTour.CurrentStepIndex);
            // Closing and reopening must preserve the task and expose a real action.
            window.Close();
            Pump();
            Assert.True(vm.RunProgramTour.ShowCardInMainWindow);
            await Open();
            window = Assert.Single(main.OwnedWindows.OfType<IsaPlaygroundWindow>());
            var picker = window.FindControl<ComboBox>("IsaSamplePicker")!;
            picker.Focus();
            window.KeyPress(Key.Home, RawInputModifiers.None);
            window.KeyRelease(Key.Home, RawInputModifiers.None);
            var sampleIndex = playground.Samples.ToList().FindIndex(s => s.FileName == RunProgramTourViewModel.MultiplySampleFileName);
            for (var i = 0; i < sampleIndex; i++)
            {
                window.KeyPress(Key.Down, RawInputModifiers.None);
                window.KeyRelease(Key.Down, RawInputModifiers.None);
            }
            Pump();
            Assert.Equal(3, vm.RunProgramTour.CurrentStepIndex);
            await Task.Delay(180); // Allow the production overlay polling to place the newly sized cue.
            Pump();
            Save(window, language, "photonic-toggle");
            UiInput.Click(window, window.FindControl<CheckBox>("IsaPhotonicToggle")!);
            Pump();
            Assert.Equal(4, vm.RunProgramTour.CurrentStepIndex);
            await Task.Delay(180); // Allow the production overlay polling to place the newly sized cue.
            Pump();
            Save(window, language, "step-adds");
            for (var i = 0; i < 60 && vm.RunProgramTour.CurrentStepIndex == 4; i++)
                UiInput.Click(window, window.FindControl<Button>("IsaStepButton")!);
            Assert.Equal(5, vm.RunProgramTour.CurrentStepIndex);
            Assert.True(playground.Accumulator > 0);
            Pump();
            await Task.Delay(180); // Allow the production overlay polling to place the newly sized cue.
            Pump();
            Save(window, language, "completed-adds");
            UiInput.Click(window, window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FinishIsaTourButton"));
            Assert.True(vm.RunProgramTour.IsCompleted);
            Assert.False(vm.RunProgramTour.IsActive);

            async Task Open()
            {
                await Task.Delay(180);
                Pump();
                UiInput.Click(main, main.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenIsaTourButton"));
                Pump();
            }
        }
        finally
        {
            foreach (var owned in main.OwnedWindows.ToArray()) owned.Close();
            main.Close();
            Pump();
            App.OverrideServicesForTesting(previousServices);
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    [AvaloniaFact]
    public void ClosedWindow_DoesNotRestartPlaybackTimerWhenSharedViewModelRunsAgain()
    {
        var playground = new IsaPlaygroundViewModel(new CAP.Avalonia.Services.ExampleDesignsService(new CAP.Avalonia.Services.FileExampleCatalog()));
        var window = new IsaPlaygroundWindow { DataContext = playground };
        window.Show();
        Pump();
        window.Close();
        playground.IsRunning = true;
        Pump();
        var timer = typeof(IsaPlaygroundWindow).GetField("_runTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window) as DispatcherTimer;
        try
        {
            Assert.False(timer?.IsEnabled ?? false,
                "a closed window must not own a second timer when the shared ISA is reopened");
        }
        finally
        {
            playground.IsRunning = false;
            timer?.Stop();
        }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string language, string step)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "isa-journey");
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, $"{language}-{step}.png"));
    }
}
