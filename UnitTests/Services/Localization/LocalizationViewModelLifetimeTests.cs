using CAP_DataAccess.Updates;
using System.Runtime.CompilerServices;
using System.Net;
using System.Net.Http;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Update;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Update;
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
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
    public async Task LiveShell_StatusOutputAndWorkspaceStillFollowLanguageChanges()
    {
        var localization = LocalizationService.Instance;
        var previous = localization.ActiveLanguageCode;
        try
        {
            localization.SetLanguage("en");
            await using var vm = MainViewModelTestHelper.CreateMainViewModel();
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
        using var handler = new NoUpdateHandler();
        using var client = new HttpClient(handler);
        var (viewModel, canvas) = await CreateDiscardedDesignAsync(client);
        handler.Calls.ShouldBe(1, "the explicit activation must complete its startup update check");
        Dispatcher.UIThread.RunJobs();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        viewModel.IsAlive.ShouldBeFalse("localization subscriptions must not root a discarded shell");
        canvas.IsAlive.ShouldBeFalse("the discarded shell's design and routing grid must also be released");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference ViewModel, WeakReference Canvas)> CreateDiscardedDesignAsync(HttpClient client)
    {
        var prefs = new UserPreferencesService(Path.Combine(Path.GetTempPath(), $"cap-lifetime-{Guid.NewGuid()}.json"));
        var update = new UpdateViewModel(new UpdateChecker(client, "test", "test"),
            new UpdateDownloader(client), prefs, Mock.Of<IUrlLauncher>(), Mock.Of<IInstaller>());
        var vm = MainViewModelTestHelper.CreateMainViewModel(preferencesService: prefs,
            updateViewModel: update, gdsExportViewModel: new NoDiscoveryExport());
        var references = (new WeakReference(vm), new WeakReference(vm.Canvas));
        try { await vm.ActivateAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await vm.DisposeAsync(); }
        // Do not let the completed async helper's state machine retain the shell.
        vm = null!;
        return references;
    }

    private sealed class NoDiscoveryExport() : GdsExportViewModel(Mock.Of<IGdsExportSession>(), Mock.Of<IPythonDiscovery>())
    {
        protected override Task<List<PythonInstallation>> DiscoverPythonsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PythonInstallation>());
    }

    private sealed class NoUpdateHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
