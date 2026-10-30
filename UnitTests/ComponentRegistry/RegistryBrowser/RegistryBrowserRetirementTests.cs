using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.ComponentRegistry.RegistryBrowser;
using CAP_Core.ComponentRegistry.RegistryClient;
using Shouldly;

namespace UnitTests.ComponentRegistry.RegistryBrowser;

public sealed class RegistryBrowserRetirementTests
{
    [AvaloniaTheory]
    [InlineData("index")]
    [InlineData("details")]
    [InlineData("preview")]
    [InlineData("download")]
    public async Task Retirement_CancelsAndDrainsEveryAcceptedStageWithoutLatePublication(string stage)
    {
        var fixture = new RegistryLifetimeFixture(stage);
        var browser = new RegistryBrowserViewModel(fixture, fixture);
        Task pending;
        if (stage == "index") pending = browser.LoadCommand.ExecuteAsync(null);
        else
        {
            await browser.LoadCommand.ExecuteAsync(null);
            if (stage == "preview") pending = browser.PreviewsLoadTask;
            else
            {
                browser.SelectedComponent = browser.Components.Single();
                if (stage == "details") pending = browser.DetailsLoadTask;
                else
                {
                    await browser.DetailsLoadTask;
                    browser.DownloadCommand.Execute(null);
                    pending = browser.DownloadTask;
                }
            }
        }
        try
        {
            await fixture.Entered.Task.WaitAsync(RegistryLifetimeFixture.Guard);
            var stopping = browser.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("feature retirement must drain the actual unfinished work");
            fixture.SeenToken.IsCancellationRequested.ShouldBeTrue();
            fixture.Release.TrySetResult();
            await stopping.WaitAsync(RegistryLifetimeFixture.Guard);
            await ObserveAsync(pending);
            switch (stage)
            {
                case "index": browser.Components.ShouldBeEmpty(); break;
                case "details": browser.Details.Manifest.ShouldBeNull(); break;
                case "preview": browser.Components.Single().PreviewSvg.ShouldBeNullOrEmpty(); break;
                case "download": browser.DownloadMessage.ShouldBeNull(); break;
            }
        }
        finally
        {
            fixture.Release.TrySetResult();
            await ObserveAsync(pending);
            await browser.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateManifest_CannotReplaceNewSelectionOrReappearAfterDeselection(bool deselect)
    {
        var fixture = new RegistryLifetimeFixture("details");
        await using var browser = new RegistryBrowserViewModel(fixture);
        await browser.LoadCommand.ExecuteAsync(null);
        browser.SelectedComponent = browser.Components.Single();
        var oldLoad = browser.DetailsLoadTask;
        try
        {
            await fixture.Entered.Task.WaitAsync(RegistryLifetimeFixture.Guard);
            browser.SelectedComponent = deselect ? null : new(new RegistryIndexEntry { Id = "b", Name = "B", Path = "b" });
            if (!deselect) await browser.DetailsLoadTask;
            fixture.Release.TrySetResult();
            await ObserveAsync(oldLoad);
            (browser.Details.Manifest?.Id).ShouldBe(deselect ? null : "b");
            browser.Details.Description.ShouldBe(deselect ? "" : "b");
        }
        finally
        {
            fixture.Release.TrySetResult();
            await ObserveAsync(oldLoad);
        }
    }

    [AvaloniaFact]
    public async Task RetiredBrowser_RejectsReloadAndSelectionWithoutNewRequests()
    {
        var fixture = new RegistryLifetimeFixture("none");
        await using var browser = new RegistryBrowserViewModel(fixture);
        await browser.StopAsync();
        browser.EnsureLoaded();
        await browser.LoadCommand.ExecuteAsync(null);
        browser.SelectedComponent = new(new RegistryIndexEntry { Id = "a", Path = "a" });
        await browser.DetailsLoadTask;
        fixture.Calls.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task ApplicationRetirement_DrainsRegisteredCatalogOwner()
    {
        var owner = new CAP.Avalonia.Services.Lifetime.ApplicationWorkCoordinator();
        var fixture = new RegistryLifetimeFixture("index");
        await using var browser = new RegistryBrowserViewModel(fixture, applicationWork: owner);
        var loading = browser.LoadCommand.ExecuteAsync(null);
        try
        {
            await fixture.Entered.Task.WaitAsync(RegistryLifetimeFixture.Guard);
            var stopping = owner.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
            fixture.Release.TrySetResult();
            await stopping.WaitAsync(RegistryLifetimeFixture.Guard);
            browser.Components.ShouldBeEmpty();
        }
        finally
        {
            fixture.Release.TrySetResult();
            await ObserveAsync(loading);
            await owner.StopAsync();
        }
    }

    private static async Task ObserveAsync(Task pending)
    {
        try { await pending.WaitAsync(RegistryLifetimeFixture.Guard); }
        catch (OperationCanceledException) { }
    }
}
