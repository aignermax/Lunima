using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Update;
using CAP.Avalonia.Views;
using CAP_Contracts;
using CAP_Core.Helpers;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Register shared HttpClient with GitHub-compatible User-Agent
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ConnectAPICPro", "1.0"));
        services.AddSingleton(httpClient);

        // Register update services
        services.AddSingleton(sp => new UpdateChecker(
            sp.GetRequiredService<HttpClient>(),
            owner: "aignermax",
            repo: "Connect-A-PIC-Pro"));
        services.AddSingleton(sp => new UpdateDownloader(
            sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IUrlLauncher, SystemUrlLauncher>();
        services.AddTransient<UpdateViewModel>();

        // Register core services
        services.AddSingleton<IDataAccessor, FileDataAccessor>();
        services.AddSingleton<CAP_Core.Components.Creation.GroupLibraryManager>();
        services.AddSingleton<GdsExportService>();
        services.AddSingleton<CAP_Core.ErrorConsoleService>();

        // Register application services
        services.AddSingleton<SimulationService>();
        services.AddSingleton<SimpleNazcaExporter>();
        services.AddSingleton<PdkLoader>();
        services.AddSingleton<Commands.CommandManager>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<ProjectPersistenceService>();
        services.AddSingleton<Services.GroupPreviewGenerator>();
        services.AddSingleton<IInputDialogService, InputDialogService>();

        // Register DesignCanvasViewModel as singleton (shared across all panel VMs)
        services.AddSingleton<DesignCanvasViewModel>();

        // Register sub-ViewModels (Left panel)
        services.AddTransient<HierarchyPanelViewModel>();
        services.AddTransient<PdkManagerViewModel>();
        services.AddTransient<ComponentLibraryViewModel>();

        // Register sub-ViewModels (Right panel)
        services.AddTransient<ParameterSweepViewModel>();
        services.AddTransient<RoutingDiagnosticsViewModel>();
        services.AddTransient<DesignValidationViewModel>();
        services.AddTransient<ComponentDimensionDiagnosticsViewModel>();
        services.AddTransient<ComponentDimensionViewModel>();
        services.AddTransient<ExportValidationViewModel>();
        services.AddTransient<SMatrixPerformanceViewModel>();
        services.AddTransient<CompressLayoutViewModel>();
        services.AddTransient<GroupSMatrixViewModel>();
        services.AddTransient<ArchitectureReportViewModel>();
        services.AddTransient<PdkConsistencyViewModel>();

        // Register sub-ViewModels (Bottom panel)
        services.AddTransient<WaveguideLengthViewModel>();
        services.AddTransient<ElementLockViewModel>();
        services.AddTransient<ErrorConsoleViewModel>();

        // Register panel ViewModels as singletons
        services.AddSingleton<LeftPanelViewModel>();
        services.AddSingleton<RightPanelViewModel>();
        services.AddSingleton<BottomPanelViewModel>();

        // Register main ViewModel
        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            singleView.MainView = new MainView
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
