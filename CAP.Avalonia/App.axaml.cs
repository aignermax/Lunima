using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.Views;
using CAP_Contracts;
using CAP_Core.Helpers;
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

        // Register core services
        services.AddSingleton<IDataAccessor, FileDataAccessor>();
        services.AddSingleton<CAP_Core.Components.Creation.GroupLibraryManager>();

        // Register application services
        services.AddSingleton<SimulationService>();
        services.AddSingleton<SimpleNazcaExporter>();
        services.AddSingleton<PdkLoader>();
        services.AddSingleton<Commands.CommandManager>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<ProjectPersistenceService>();
        services.AddSingleton<Services.GroupPreviewGenerator>();
        services.AddSingleton<IInputDialogService, InputDialogService>();

        // Register ViewModels
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
