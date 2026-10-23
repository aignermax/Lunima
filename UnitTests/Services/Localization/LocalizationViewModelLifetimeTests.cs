using CAP_DataAccess.Updates;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Update;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Services.Localization;

/// <summary>Process-wide localization must not retain discarded design graphs.</summary>
[Collection("LocalizationSingleton")]
public class LocalizationViewModelLifetimeTests
{
    [AvaloniaFact]
    public void LiveShell_StatusOutputAndWorkspaceStillFollowLanguageChanges()
    {
        var localization = LocalizationService.Instance;
        var previous = localization.ActiveLanguageCode;
        try
        {
            localization.SetLanguage("en");
            var vm = MainViewModelTestHelper.CreateMainViewModel();
            var output = vm.BottomPanel.Analysis.Output;
            var originalOutput = output.OutputDisplayName;
            var originalMode = vm.StatusBar.ModeLabel;
            var notifications = new List<string?>();
            vm.WorkspaceSwitch.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

            localization.SetLanguage("de");

            output.OutputDisplayName.ShouldNotBe(originalOutput);
            output.OutputDisplayName.ShouldBe(localization.Translate("Analysis.Output.None"));
            vm.StatusBar.ModeLabel.ShouldNotBe(originalMode);
            vm.StatusBar.ModeLabel.ShouldBe(localization.Translate("Canvas.Mode.Select"));
            notifications.ShouldContain(nameof(vm.WorkspaceSwitch.RunLabel));
            notifications.ShouldContain(nameof(vm.WorkspaceSwitch.DisplayName));
        }
        finally
        {
            localization.SetLanguage(previous);
        }
    }

    [AvaloniaFact]
    public async Task DiscardedMainViewModelAndCanvas_AreCollectible()
    {
        using var client = new HttpClient(new NoUpdateHandler());
        var (viewModel, canvas, startupFinished) = CreateDiscardedDesign(client);
        // The constructor's delayed update task legitimately owns the shell until it finishes.
        await startupFinished.WaitAsync(TimeSpan.FromSeconds(10));
        Dispatcher.UIThread.RunJobs();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        viewModel.IsAlive.ShouldBeFalse("localization subscriptions must not root a discarded shell");
        canvas.IsAlive.ShouldBeFalse("the discarded shell's design and routing grid must also be released");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference ViewModel, WeakReference Canvas, Task StartupFinished) CreateDiscardedDesign(HttpClient client)
    {
        var prefs = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"cap-lifetime-{Guid.NewGuid()}.json"));
        var update = new UpdateViewModel(new UpdateChecker(client, "test", "test"),
            new UpdateDownloader(client), prefs, Mock.Of<IUrlLauncher>(), Mock.Of<IInstaller>());
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool started = false;
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName != nameof(UpdateViewModel.IsChecking)) return;
            if (update.IsChecking) started = true;
            else if (started)
            {
                update.PropertyChanged -= handler;
                finished.SetResult();
            }
        };
        update.PropertyChanged += handler;
        var vm = MainViewModelTestHelper.CreateMainViewModel(preferencesService: prefs, updateViewModel: update);
        return (new WeakReference(vm), new WeakReference(vm.Canvas), finished.Task);
    }

    private sealed class NoUpdateHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
