using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CAP.Avalonia.DI;
using CAP.Avalonia.Services.DialogSizing;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private Services.Lifetime.ApplicationLifetimeCoordinator? _lifetime;

    /// <summary>Single-view hosts await this before unloading the application; desktop close uses the same owner.</summary>
    public Task RetireAsync() => _lifetime?.StopAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Test seam (InternalsVisibleTo UnitTests): headless UI-flow tests run without
    /// <see cref="OnFrameworkInitializationCompleted"/>, so they install their own provider here —
    /// Test composition resolves window factories through <see cref="Services"/>.
    /// </summary>
    internal static void OverrideServicesForTesting(IServiceProvider services) => Services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Registers every feature's services into <paramref name="services"/>.
    /// Extracted from <see cref="OnFrameworkInitializationCompleted"/> so the full DI
    /// graph can be built and validated in a headless test (catches a missing/misplaced
    /// registration that would otherwise only surface as a crash on app start).
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddUpdateFeature();
        services.AddAiAssistantFeature();
        services.AddAgentBridgeFeature();
        services.AddExportFeature();
        services.AddPythonEnvFeature();
        services.AddCoreServices();
        services.AddCanvasAndPanels();
        services.AddCrossingInsertionFeature();
        services.AddSettingsFeature();
        services.AddPdkOffsetFeature();
        services.AddPdkResolutionFeature();
        services.AddFdtdFeature();
        services.AddModeSolverFeature();
        services.AddNotificationFeature();
        services.AddHomeFeature();
        services.AddOnboardingFeature();
        services.AddComponentRegistryFeature();
        services.AddAddCustomComponentFeature();
        services.AddGdsImportFeature();
        services.AddIsaPlaygroundFeature();
        services.AddLocalizationFeature();

        services.AddSingleton<MainViewModel>();
    }

    /// <summary>
    /// Eagerly resolves DI singletons whose only job is a side effect wired up by their
    /// constructor, with no other code path holding a reference to them — so nothing else
    /// in the graph guarantees they get built. Extracted from
    /// <see cref="OnFrameworkInitializationCompleted"/> so a headless test can assert the
    /// guarantee without needing the full Avalonia application lifecycle.
    /// </summary>
    public static void ActivateStartupSingletons(IServiceProvider services)
    {
        // Restores the persisted Settings → Routing → Crossings toggle and wires the
        // crossing-insertion service into the canvas — must happen before the first
        // routing pass, independent of whether any ViewModel happens to reference it.
        services.GetRequiredService<ViewModels.Canvas.CrossingInsertion.CrossingInsertionCanvasBinder>();

        // Starts the local MCP agent bridge when the user enabled it in Settings (opt-in;
        // a disabled bridge opens no socket).
        services.GetRequiredService<Services.AgentBridge.Hosting.AgentBridgeHost>().ApplySettings();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Issue #697: re-enforce requested dialog sizes after open (Linux/X11 collapse).
        DialogSizeGuard.Initialize();

        var services = new ServiceCollection();
        ConfigureServices(services);
        var provider = services.BuildServiceProvider();
        Services = provider;
        ActivateStartupSingletons(Services);

        // Error-console entries are bound to the UI, but simulations/renders log from
        // worker threads — marshal every append through the UI dispatcher.
        Services.GetRequiredService<CAP_Core.ErrorConsoleService>().PostToUiThread =
            action => global::Avalonia.Threading.Dispatcher.UIThread.Post(action);

        // Apply the persisted UI language (or auto-detect the OS display language when
        // set to "system") before any window binds its localized strings (issue #744).
        Services.GetRequiredService<Services.Localization.LocalizationService>()
            .SetLanguage(Services.GetRequiredService<Services.UserPreferencesService>().GetUiLanguage());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            _lifetime = new Services.Lifetime.ApplicationLifetimeCoordinator(mainVm,
                Services.GetRequiredService<Services.AgentBridge.Hosting.AgentBridgeHost>().RetireAsync,
                provider.DisposeAsync, Services.GetRequiredService<Services.Lifetime.ApplicationWorkCoordinator>());
            // A .lun file passed on the command line (or via OS file association)
            // is opened by MainWindow's Loaded handler.
            mainVm.StartupDesignFile = CAP.Avalonia.Services.DesignFileArguments
                .FindDesignFile(desktop.Args ?? Array.Empty<string>());
            var window = MainWindowComposition.Create(Services, mainVm, RetireAsync);
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await _lifetime.ActivateAsync();
            // OS shutdown requests must pass through the same save/discard/cancel guard.
            desktop.ShutdownRequested += (_, args) => { args.Cancel = true; window.Close(); };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var mainVm = Services.GetRequiredService<MainViewModel>();
            _lifetime = new Services.Lifetime.ApplicationLifetimeCoordinator(mainVm,
                Services.GetRequiredService<Services.AgentBridge.Hosting.AgentBridgeHost>().RetireAsync,
                provider.DisposeAsync, Services.GetRequiredService<Services.Lifetime.ApplicationWorkCoordinator>());
            singleView.MainView = new MainView
            {
                DataContext = mainVm
            };
            singleView.MainView.Loaded += async (_, _) => await _lifetime.ActivateAsync();
            singleView.MainView.Unloaded += async (_, _) => await RetireAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
