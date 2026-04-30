using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP.Avalonia.Views;

namespace CAP.Avalonia.Services;

/// <summary>
/// Avalonia implementation of <see cref="IPortMappingDialogService"/>.
/// Spins up a <see cref="PortMappingDialog"/> rooted at the application's
/// main window and awaits its result.
/// </summary>
public class PortMappingDialogService : IPortMappingDialogService
{
    public async Task<IReadOnlyDictionary<string, string>?> ShowAsync(
        string componentDisplayName,
        IReadOnlyList<string> importedNames,
        IReadOnlyList<string> componentPinNames)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;
        if (desktop.MainWindow is not { } owner)
            return null;

        var vm = new PortMappingDialogViewModel();
        vm.Configure(importedNames, componentPinNames, componentDisplayName);

        var dialog = new PortMappingDialog { DataContext = vm };
        var result = await dialog.ShowDialog<IReadOnlyDictionary<string, string>?>(owner);
        return result;
    }
}
