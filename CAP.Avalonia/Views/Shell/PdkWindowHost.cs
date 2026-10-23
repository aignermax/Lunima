using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Process;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns PDK editor instances and the dialogs that mutate the managed PDK library.</summary>
internal sealed class PdkWindowHost(
    Window owner, CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel library,
    CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel canvas,
    Func<Component, string?> resolvePdkSource, IUserPdkLibrary? store, CAP_Core.ErrorConsoleService errors,
    ProcessEditorFactory? processEditors = null)
{
    private readonly Window _owner = owner;
    private readonly CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel _library = library;
    private readonly CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel _canvas = canvas;
    private readonly Func<Component, string?> _resolvePdkSource = resolvePdkSource;
    private readonly IUserPdkLibrary? _store = store;
    private readonly CAP_Core.ErrorConsoleService _errors = errors;
    private ProcessManagementViewModel CreateProcessEditor() =>
        processEditors?.Create(new FileDialogService(_owner))
        ?? new ProcessManagementViewModel(new FileDialogService(_owner), _store);

    private readonly Dictionary<string, ProcessManagementWindow> _openPdkEditWindows = new();

    /// <summary>
    /// Handles "Edit…" click on a custom PDK's row in PDK Management (issue #726 follow-up):
    /// opens the Fabrication Process editor scoped to just that PDK's own process, replacing the
    /// old toolbar-wide dialog with its preset/import pickers. Bundled PDKs have no Edit button
    /// (see the <c>!IsBundled</c> visibility binding in <c>MainWindow.axaml</c>), but the check is
    /// repeated here as the authoritative guard. Never touches
    /// <c>FileOperationsViewModel.ActiveProcess</c> — only the PDK's own JSON file.
    /// </summary>
    public void EditProcess(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PdkInfoViewModel pdk })
            return;

        // Match by file path first, not just display name: two loaded PDKs can share a name
        // (e.g. two custom PDKs authored under the same name from different files), and a
        // name-only lookup could then load the wrong draft here while still resolving
        // PdkFilePathResolver to this row's OWN file below — silently writing this edit into a
        // different PDK's JSON (issue #733 review, Finding 5).
        var draft = MetalTraceStyleResolver.FindOwnDraft(_library.GetLoadedPdkDrafts(), pdk.FilePath, pdk.Name);
        if (draft is null)
            return;

        // Dedup: a second click on the same PDK's "Edit…" button re-activates the
        // already-open editor instead of spawning a duplicate window. Deliberately shows the
        // window's CURRENT (possibly unsaved) editor state rather than reloading the draft —
        // reloading would silently destroy the user's in-progress edits.
        var key = pdk.FilePath ?? pdk.Name;
        if (_openPdkEditWindows.TryGetValue(key, out var existingWindow) && existingWindow.IsVisible)
        {
            // Un-minimize first: Activate() alone leaves a minimized window minimized,
            // which looks like the button silently did nothing.
            existingWindow.WindowState = WindowState.Normal;
            existingWindow.Activate();
            return;
        }

        var processVm = CreateProcessEditor();

        // Resolve straight to this row's own file path — no name-based lookup needed since
        // the button is already scoped to this exact PDK.
        processVm.PdkFilePathResolver = _ => pdk.FilePath;
        // Confirm before overwriting the PDK's JSON on disk (same prompt as the former
        // toolbar dialog): naming the exact file so it can't be edited by accident.
        processVm.ConfirmSaveToPdk = async path =>
        {
            var choice = await new MessageBoxService().ShowChoicePromptAsync(
                $"This overwrites the PDK file on disk:\n{path}\n\nOnly this process's own "
                + "layers and cross-sections are written. Continue?",
                "Save to PDK file?", new[] { "Cancel", "Save" });
            return choice == 1;
        };
        processVm.LoadForSinglePdkEdit(draft);
        // Re-apply the active process lock by value after a save, so an edit that changes this
        // PDK's fingerprint is reflected immediately without a restart.
        processVm.ProcessSaved += async (_, _) =>
        {
            _library.ReapplyActiveProcessAfterPdkChange();
            await WarnIfSavedProcessDivergedFromDesign(pdk);
        };
        // A first save on a bundled PDK implicitly wrote a fork — swap the library entry to it
        // (same name, so the active process keeps resolving by value).
        processVm.BundledPdkForkSaved += (_, args) =>
            _library.RegisterSavedPdkFork(args.PdkName, args.ForkPath);

        var processWindow = new ProcessManagementWindow
        {
            DataContext = processVm,
            Title = $"Edit Process — {pdk.Name}",
        };
        _openPdkEditWindows[key] = processWindow;
        // Only remove the entry if it still points at THIS window: if the key was ever
        // re-assigned to a newer window, the older window's Closed handler must not
        // deregister the newer one.
        processWindow.Closed += (_, _) =>
        {
            if (_openPdkEditWindows.TryGetValue(key, out var tracked) && ReferenceEquals(tracked, processWindow))
                _openPdkEditWindows.Remove(key);
        };
        processWindow.Show(_owner);
    }

    /// <summary>
    /// Warns the user when a per-PDK process save (<see cref="EditProcess"/>) diverged
    /// <paramref name="pdk"/> from the design's active process (issue #570 follow-up, LC-T4):
    /// the placement lock (<c>IsLockedByProcess</c>, just recomputed by
    /// <see cref="CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel.ReapplyActiveProcessAfterPdkChange"/>) already blocks NEW
    /// placements from this PDK, but components placed from it BEFORE the edit are deliberately
    /// kept on the canvas — this tells the user they are now in conflict instead of leaving that
    /// discoverable only via Design Checks. No dialog when the PDK isn't locked (no divergence)
    /// or there are zero placed components from it (the lock alone is enough). Never deletes
    /// anything.
    /// </summary>
    private async Task WarnIfSavedProcessDivergedFromDesign(PdkInfoViewModel pdk)
    {
        var pdkInfo = _library.PdkManager.LoadedPdks.FirstOrDefault(p =>
            pdk.FilePath != null ? p.FilePath == pdk.FilePath : p.Name == pdk.Name);
        if (pdkInfo is not { IsLockedByProcess: true })
            return;

        var conflictedCount = _canvas.Components.Count(c =>
            (c.TemplatePdkSource ?? _resolvePdkSource(c.Component))
            == pdkInfo.Name);
        if (conflictedCount == 0)
            return;

        await new MessageBoxService().ShowChoicePromptAsync(
            "The saved process no longer matches the design's active process. "
            + $"{conflictedCount} placed component(s) from '{pdkInfo.Name}' are now in conflict "
            + "and new placements are blocked. Existing components are kept — see Design Checks.",
            "Process Changed", new[] { "OK" });
    }

    /// <summary>
    /// Handles "Delete…" click on a custom PDK's row in PDK Management (LC-T5): after a confirm
    /// prompt, moves the whole PDK file to <c>user-pdks/.trash</c> via <see cref="IUserPdkLibrary"/>
    /// and then deregisters it from the library (templates, PDK-manager entry, in-memory draft,
    /// remembered import path) via <see cref="CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel.UnregisterPdk"/> — mirrors
    /// <see cref="CreateAsync"/>'s store-then-register order, just in reverse. Bundled PDKs
    /// have no Delete button (see the <c>!IsBundled</c> visibility binding in
    /// <c>MainWindow.axaml</c>), but the check is repeated here as the authoritative guard.
    /// Placed components on the canvas are never touched (Design Checks flag any resulting
    /// conflict, mirroring <see cref="WarnIfSavedProcessDivergedFromDesign"/>).
    /// </summary>
    public async void DeletePdk(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PdkInfoViewModel pdk } || pdk.IsBundled || pdk.FilePath is null)
            return;

        var userPdkStore = _store;
        if (userPdkStore is null)
            return;

        // Only files inside the managed user-pdks root are moved to its .trash. An
        // externally-stored PDK (imported from an arbitrary folder, remembered via preferences)
        // is the user's own file in a place they chose — deleting it from the library must not
        // relocate it into a hidden app-data folder (PR #739 review), so that path only
        // deregisters and leaves the file untouched.
        var isManaged = userPdkStore.IsInManagedRoot(pdk.FilePath);
        // A fork of a bundled PDK "reverts to the foundry truth" instead: the user's copy is
        // removed and the read-only built-in original reappears in the library.
        var bundledCount = pdk.ShadowsBundledPdk
            ? _library.GetBundledOriginalComponentCount(pdk.Name)
            : null;
        var isRevertToBundled = bundledCount is not null;
        var prompt = isRevertToBundled
            ? $"Remove your customized copy of '{pdk.Name}' and restore the built-in original (its {bundledCount} components)?\n\n"
              + (isManaged
                  ? "Your copy is moved to the trash."
                  : $"Your file stays untouched at:\n{pdk.FilePath}")
            : isManaged
                ? $"Move '{pdk.Name}' ({pdk.ComponentCount} components) to trash?\n\n"
                  + "The file is moved to user-pdks/.trash and can be restored manually."
                : $"Remove '{pdk.Name}' ({pdk.ComponentCount} components) from the library?\n\n"
                  + $"The file stays untouched at:\n{pdk.FilePath}";
        var confirmLabel = isRevertToBundled ? "Restore Original" : isManaged ? "Move to Trash" : "Remove";
        var choice = await new MessageBoxService().ShowChoicePromptAsync(
            prompt, "Delete PDK?", new[] { "Cancel", confirmLabel });
        if (choice != 1)
            return;

        if (isRevertToBundled && isManaged)
        {
            // The revert is all-or-nothing: a false here means nothing was changed —
            // tell the user instead of closing as if it worked.
            if (!_library.RevertShadowForkToBundled(pdk))
                await ShowRestoreOriginalFailedAsync(pdk.Name);
            return;
        }

        if (isManaged)
        {
            try
            {
                userPdkStore.MoveToTrash(pdk.FilePath);
            }
            catch (Exception ex)
            {
                _errors.LogError($"Failed to move PDK '{pdk.Name}' to trash: {ex.Message}", ex);
                return;
            }
        }

        _library.UnregisterPdk(pdk.FilePath);
        if (isRevertToBundled && !_library.RestoreBundledPdk(pdk.Name))
            await ShowRestoreOriginalFailedAsync(pdk.Name);
    }

    private static async Task ShowRestoreOriginalFailedAsync(string pdkName)
    {
        await new MessageBoxService().ShowChoicePromptAsync(
            $"Could not restore the built-in original of '{pdkName}'.\n\n"
            + "See the Error Console for details.",
            "Restore failed", new[] { "OK" });
    }

    /// <summary>
    /// The single shared wiring for the Create-Custom-PDK dialog — used by both entry points
    /// (the PDK-Management "+" button above and the New Component assistant's "New PDK…"
    /// sentinel), so their importer lists, event wiring, and post-create registration can never
    /// drift apart (PR #739 review). Shows the dialog modally over <paramref name="owner"/>;
    /// on success the (possibly component-less) new PDK is registered straight into the library
    /// via <see cref="CAP.Avalonia.ViewModels.Panels.LeftPanelViewModel.RegisterCreatedPdk"/> — so it appears in PDK Management
    /// immediately regardless of entry point — and its file path is returned. Returns null when
    /// the user cancelled or the dialog faulted (logged, not swallowed).
    /// </summary>
    public async Task<string?> CreateAsync(Window owner)
    {

        var userPdkStore = _store;
        if (userPdkStore is null)
            return null;

        var availableProcesses = _library.GetLoadedPdkDrafts()
            .Where(d => d.Process != null && !d.ProcessAgnostic)
            .Select(d => d.Process!)
            .ToList();
        var processDefinitionEditor = CreateProcessEditor();

        // Loaded bundled PDK names are reserved: a user PDK under such a name would be taken
        // for a fork of the built-in PDK and shadow it.
        var reservedBundledNames = _library.PdkManager.LoadedPdks
            .Where(p => p.IsBundled || p.ShadowsBundledPdk)
            .Select(p => p.Name)
            .ToList();
        var createVm = new CreateCustomPdkViewModel(
            userPdkStore, availableProcesses, processDefinitionEditor, reservedBundledNames);
        var createWindow = new CreateCustomPdkWindow { DataContext = createVm };

        string? createdPath = null;
        createVm.PdkCreated += (_, path) =>
        {
            createdPath = path;
            createWindow.Close();
        };

        try
        {
            await createWindow.ShowDialog(owner);
        }
        catch (Exception ex)
        {
            _errors.LogError($"Create-PDK dialog failed: {ex.Message}", ex);
            return null;
        }

        if (createdPath is not null)
            _library.RegisterCreatedPdk(createdPath);
        return createdPath;
    }
}
