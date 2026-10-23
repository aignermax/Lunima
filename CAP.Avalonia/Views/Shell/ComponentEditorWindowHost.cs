using Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP_DataAccess.Components.AddCustomComponent;
using System.IO;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns component-editor identity, dirty-editor reuse and cancellation on close.</summary>
internal sealed class ComponentEditorWindowHost(
    Window owner, CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel library,
    ComponentSettingsWindowHost settings, PdkWindowHost pdks, IUserPdkLibrary? store, Action openSolverSettings)
{
    private readonly Window _owner = owner;
    private readonly CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel _library = library;
    private readonly ComponentSettingsWindowHost _settings = settings;
    private readonly PdkWindowHost _pdks = pdks;
    private readonly IUserPdkLibrary? _store = store;
    private readonly Action _openSolverSettings = openSolverSettings;
    private readonly Dictionary<string, NewComponentWindow> _openComponentEditWindows = new();

    public async Task ShowAsync(NewComponentViewModel newComponentVm)
    {
        // By the time this hook runs, LoadForEdit was already applied — IsEditMode
        // and the edit-identity fields are set, so the dedup key can be built here.
        var editKey = newComponentVm.IsEditMode && newComponentVm.EditOriginalPdkKey is not null
            && newComponentVm.EditingOriginalName is not null
            ? $"{newComponentVm.EditOriginalPdkKey}::{newComponentVm.EditingOriginalName}"
            : null;
        if (editKey is not null
            && _openComponentEditWindows.TryGetValue(editKey, out var existingComponentWindow)
            && existingComponentWindow.IsVisible)
        {
            // The existing window may hold an outdated snapshot (file changed on
            // disk since it opened) — saving from it would overwrite the newer
            // state. If the user has no unsaved input, adopt the freshly loaded
            // view model WHOLESALE (a field-by-field copy would lose the edit
            // bookkeeping); otherwise only activate — input is never thrown away.
            if (existingComponentWindow.DataContext is NewComponentViewModel existingVm
                && !existingVm.HasUnsavedEditChanges)
            {
                WireNewComponentEditorHooks(newComponentVm, existingComponentWindow);
                // The replaced view model is dropped here — cancel any Meep compute
                // it is still burning CPU on and release its registry subscription,
                // or it keeps solving/probing availability for a dead editor.
                existingComponentWindow.IsEnabled = false;
                await existingVm.DisposeAsync();
                if (!existingComponentWindow.IsVisible || existingComponentWindow.IsRetiring)
                {
                    await newComponentVm.DisposeAsync();
                    return;
                }
                existingComponentWindow.DataContext = newComponentVm;
                existingComponentWindow.IsEnabled = true;
            }
            else
            {
                // The dirty editor keeps its view model — the freshly built one is
                // dropped, so release its registry subscription or it keeps firing
                // phantom availability probes forever.
                await newComponentVm.DisposeAsync();
            }
            // Un-minimize first: Activate() alone leaves a minimized window
            // minimized, which looks like the ✏ button silently did nothing.
            existingComponentWindow.WindowState = WindowState.Normal;
            existingComponentWindow.Activate();
            return;
        }

        var window = new NewComponentWindow();
        WireNewComponentEditorHooks(newComponentVm, window);
        window.DataContext = newComponentVm;
        // The window retires its current model only after the close was approved.
        if (editKey is not null)
        {
            _openComponentEditWindows[editKey] = window;
            // Only remove the entry if it still points at THIS window: if the key
            // was ever re-assigned to a newer window, the older window's Closed
            // handler must not deregister the newer one.
            window.Closed += (_, _) =>
            {
                if (_openComponentEditWindows.TryGetValue(editKey, out var tracked) && ReferenceEquals(tracked, window))
                    _openComponentEditWindows.Remove(editKey);
            };
        }
        window.Show(_owner);
    }

    /// <summary>
    /// Wires one <see cref="NewComponentViewModel"/> to the window that hosts it. Runs both for
    /// a brand-new editor window and when the editor dedup swaps a freshly loaded view model
    /// into an already-open window, so the two paths can never drift apart.
    /// </summary>
    private void WireNewComponentEditorHooks(NewComponentViewModel newComponentVm, NewComponentWindow window)
    {
        // Deep-link for the missing-key hint: opens Settings on the Tidy3D Cloud page.
        if (newComponentVm.BackendSelection != null)
        {
            newComponentVm.BackendSelection.OpenTidy3dSettingsPage = _openSolverSettings;
        }
        // Own-code mode's "Load from .py…" button (#custom-component-rawcode): the view model
        // only knows the file's already-read contents (PickPyFile's contract), never a path,
        // so it can't own a FileDialogService itself.
        newComponentVm.PickPyFile = async () =>
        {
            var path = await new FileDialogService(_owner).ShowOpenFileDialogAsync(
                "Load Python file", "Python Files (*.py)|*.py|All Files (*.*)|*.*");
            return path is null ? null : await File.ReadAllTextAsync(path);
        };
        newComponentVm.PickSMatrixFile = () => new FileDialogService(_owner).ShowOpenFileDialogAsync(
            "Load S-Parameter File",
            "S-Parameter Files|*.sparam;*.dat;*.txt;*.s1p;*.s2p;*.s3p;*.s4p;*.sNp|All Files|*.*");
        newComponentVm.ShowStoredSMatrices = (pdkName, compName) =>
        {
            var template = _library.AllTemplates
                .FirstOrDefault(t => t.Name == compName && t.PdkSource == pdkName);
            _settings.Show($"{pdkName}::{compName}", compName, null, template);
            return System.Threading.Tasks.Task.CompletedTask;
        };
        // Confirm before overwriting an existing component name in the target PDK
        // (new or existing custom PDK — the message names whichever applies).
        newComponentVm.ConfirmOverwrite = async (name, pdkName) =>
        {
            var choice = await new MessageBoxService().ShowChoicePromptAsync(
                $"'{name}' already exists in PDK '{pdkName}'. Overwrite?",
                "Overwrite?", new[] { "Cancel", "Overwrite" });
            return choice == 1;
        };
        newComponentVm.Saved += (_, _) => window.Close();
        // "New PDK…" opens the purpose-built CreateCustomPdkWindow, not the general Fabrication
        // Process editor. Modal with the New Component window as owner, so its PDK dropdown
        // cannot be left mid-selection while the modal is open.
        newComponentVm.CreateNewPdk = async () =>
        {
            // ShowCreateCustomPdkDialogAsync also registers the new (possibly component-less)
            // PDK, so cancelling the component afterwards doesn't leave the PDK invisible.
            var createdPath = await _pdks.CreateAsync(window);
            if (createdPath is null)
                return null;

            var userPdkStore = _store;
            return userPdkStore?.ListCustomPdks().FirstOrDefault(i => i.FilePath == createdPath);
        };
    }
}
