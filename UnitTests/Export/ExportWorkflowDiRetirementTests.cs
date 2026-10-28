using Avalonia.Headless.XUnit;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using CAP_Core.Export.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace UnitTests.Export;

public sealed class ExportWorkflowDiRetirementTests
{
    [AvaloniaTheory]
    [InlineData("nazca")]
    [InlineData("gdsfactory")]
    [InlineData("openebl")]
    public async Task ConfiguredFeatureOwner_ParticipatesInActualApplicationRetirement(string feature)
    {
        await using var fixture = new ExportWorkflowLifetimeFixture();
        var application = new ApplicationWorkCoordinator();
        var services = new ServiceCollection();
        services.AddExportFeature();
        services.AddSingleton(application);
        services.AddSingleton(fixture.Canvas);
        services.AddSingleton<IGdsScriptRunner>(fixture.Runner);
        services.AddSingleton<IGdsExportSession>(fixture.Runner);
        services.AddSingleton(Mock.Of<IPythonDiscovery>());
        services.AddSingleton(fixture.Launcher.Object);
        services.AddSingleton(new UserPreferencesService(Path.Combine(fixture.DirectoryPath, "preferences.json")));
        services.AddSingleton(new CAP_Core.ErrorConsoleService());
        services.AddSingleton(new SimpleNazcaExporter());
        services.AddSingleton(OpenEblWindowLifetimeTests.Checker());
        await using var provider = services.BuildServiceProvider();
        Task running;
        if (feature == "nazca")
            running = provider.GetRequiredService<GdsExportViewModel>().ExportScriptToGdsAsync(fixture.ScriptPath);
        else if (feature == "gdsfactory")
        {
            var vm = provider.GetRequiredService<GdsFactoryExportViewModel>();
            vm.TemplateLibraryProvider = () => [];
            vm.FileDialogService = new ExportWorkflowLifetimeFixture.Picker(fixture.ScriptPath);
            running = vm.ExportCommand.ExecuteAsync(null);
        }
        else
        {
            var vm = provider.GetRequiredService<OpenEblCheckViewModel>();
            vm.FileName = $"EBeam_di_lifetime_{Guid.NewGuid():N}";
            running = vm.CheckAnywayCommand.ExecuteAsync(null);
        }
        try
        {
            await fixture.Runner.Started.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            var stopping = application.StopAsync();
            stopping.IsCompleted.ShouldBeFalse("the registered feature still owns the accepted runner");
            await fixture.Runner.Canceled.Task.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            fixture.Runner.Release.TrySetResult();
            await stopping.WaitAsync(ExportWorkflowLifetimeFixture.Guard);
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
        }
        finally
        {
            fixture.Runner.Release.TrySetResult();
            await ExportWorkflowLifetimeFixture.FinishAsync(running);
            await application.StopAsync();
            if (feature == "openebl") OpenEblWindowLifetimeTests.RemoveArtifacts(fixture);
        }
    }
}
