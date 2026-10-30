using Avalonia.Controls;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.Views.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>Owns one tool-window feature scope without retaining transient models in the root container.</summary>
internal static class ModeSolverWindowComposition
{
    public static async Task ShowAsync(IServiceProvider services, Window owner)
    {
        if (await CreateAsync(services) is not { } window) return;
        try { window.Show(owner); }
        catch
        {
            await window.RetireFeatureAsync!();
            throw;
        }
    }

    internal static async Task<ModeSolverDialog?> CreateAsync(IServiceProvider services)
    {
        var scope = services.CreateAsyncScope();
        try
        {
            var model = scope.ServiceProvider.GetService<ModeSolverViewModel>();
            if (model == null)
            {
                await scope.DisposeAsync();
                return null;
            }
            var lifetime = new DialogScope(model, scope);
            var window = new ModeSolverDialog { DataContext = model, RetireFeatureAsync = lifetime.StopAsync };
            lifetime.DisableWindow = () => window.IsEnabled = false;
            lifetime.Register(services.GetService<ApplicationWorkCoordinator>());
            return window;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class DialogScope(ModeSolverViewModel model, AsyncServiceScope scope)
    {
        private IDisposable? _registration;
        private Task? _retirement;
        public Action? DisableWindow { get; set; }

        public void Register(ApplicationWorkCoordinator? application) =>
            _registration = application?.Register(StopAsync);

        public Task StopAsync() => _retirement ??= RetireAsync();

        private async Task RetireAsync()
        {
            DisableWindow?.Invoke();
            try { await model.StopAsync(); }
            finally
            {
                try { await scope.DisposeAsync(); }
                finally
                {
                    _registration?.Dispose();
                    _registration = null;
                    DisableWindow = null;
                }
            }
        }
    }
}
