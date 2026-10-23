using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.PdkResolution;
using CAP.Avalonia.ViewModels.Settings;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.Views.Dialogs;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Constructs tool windows with their feature models, leaving window reuse to the shell.</summary>
internal sealed class ShellToolWindowFactory(
    Func<SettingsWindowViewModel>? settings = null,
    Func<Window, Task>? modes = null,
    Func<IsaPlaygroundViewModel?>? playground = null,
    Func<RunProgramTourViewModel?>? programTour = null,
    Func<PdkResolutionCheckViewModel?>? pdkCheck = null,
    Func<OpenEblCheckViewModel?>? openEbl = null)
{
    public SettingsWindowHost CreateSettings(Window owner, Action<string> status) => new(owner,
        settings ?? (() => throw new InvalidOperationException("Settings are unavailable in this host.")), status);

    public Task ShowModeSolverAsync(Window owner) => modes?.Invoke(owner) ?? Task.CompletedTask;

    public IsaPlaygroundWindow? CreatePlayground() => playground?.Invoke() is { } model
        ? new IsaPlaygroundWindow { DataContext = model, Tour = programTour?.Invoke() } : null;

    public void ShowPdkCheck(Window owner)
    {
        if (pdkCheck?.Invoke() is { } model) new PdkResolutionCheckDialog { DataContext = model }.Show(owner);
    }

    public async Task ShowOpenEblAsync(Window owner, string? designName)
    {
        if (openEbl?.Invoke() is not { } model) return;
        model.PrepareForOpen(designName);
        await new OpenEblCheckDialog { DataContext = model }.ShowDialog(owner);
    }
}
