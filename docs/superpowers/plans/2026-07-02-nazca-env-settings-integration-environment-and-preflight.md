# Environment selection and preflight

> Historical July 2026 implementation plan. [Plan index](2026-07-02-nazca-env-settings-integration.md).
> For current design rules, see [Architecture](../../../ARCHITECTURE.md) and [Codebase map](../../CODEBASE_MAP.md).

# Nazca-Environment-Integration in Settings & Export-Flow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Python-environment management out of the Properties sidebar into a dedicated Settings page, surface managed Nazca environments as selectable candidates on the GDS-Export settings page (with a "Create + install Nazca" fallback), and guard the Nazca/GDS export with an install prompt when no Nazca is available.

**Architecture:** All new logic lives in the existing ViewModels (`GdsExportViewModel`, `PythonEnvironmentManagerViewModel`, `SettingsWindowViewModel`); cross-slice access goes through DI-injected delegates, never direct imports from the GDS-export slice into the env-manager slice. UI reuses the existing `PythonEnvironmentManagerPanel` UserControl inside a Settings `DataTemplate`.

**Tech Stack:** C# / Avalonia / CommunityToolkit.Mvvm, xUnit + Shouldly, `python3 tools/smart_test.py` bzw. `py %USERPROFILE%\.cap-tools\smart_test.py` als Testrunner.

**Spec:** `docs/superpowers/specs/2026-07-02-nazca-env-settings-integration-design.md`

## Global Constraints

- Branch: `feat/nazca-env-settings-integration` (von `main` nach Merge von #598).
- Tests IMMER via `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" <Pattern>` (nie `dotnet test` direkt).
- Kein `new ProcessStartInfo` / `Process.Start("` in Produktionscode (Architektur-Test).
- Max 250 Zeilen pro NEUER Datei; XML-Doku auf allen public Members.
- Der reine Nazca-Skript-Export muss ohne Python/Nazca funktionieren wie bisher.
- Commit-Präfixe: `(+)` neue Funktionalität, `(=)` Bugfix, `(~)` Refactoring, `(c)` Doku/Chore.

---

### Task 1: `IMessageBoxService.ShowChoicePromptAsync` (Drei-Knopf-Dialog)

**Files:**
- Modify: `CAP.Avalonia/Services/IMessageBoxService.cs`
- Modify: `CAP.Avalonia/Services/MessageBoxService.cs`

**Interfaces:**
- Produces: `Task<int> ShowChoicePromptAsync(string message, string title, IReadOnlyList<string> buttonLabels)` — Rückgabe: Index des geklickten Buttons, `-1` wenn das Fenster geschlossen wurde. Von Task 3 (Preflight) und Task 8 (Export-Guard) konsumiert.

Kein Unit-Test (öffnet ein echtes Avalonia-Fenster); Konsumenten testen gegen einen Fake. Manuelle Verifikation in Task 9.

- [ ] **Step 1: Interface erweitern**

In `IMessageBoxService.cs` am Ende des Interfaces ergänzen:

```csharp
    /// <summary>
    /// Shows a dialog with one button per entry in <paramref name="buttonLabels"/>.
    /// </summary>
    /// <param name="message">Message to display.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="buttonLabels">Button captions, left to right.</param>
    /// <returns>Index of the clicked button, or -1 if the dialog was closed.</returns>
    Task<int> ShowChoicePromptAsync(string message, string title, IReadOnlyList<string> buttonLabels);
```

- [ ] **Step 2: Implementierung in `MessageBoxService`**

Spiegelt `ShowSavePromptAsync` (gleiches Fenster-Layout, gleiche Farben):

```csharp
    /// <inheritdoc/>
    public async Task<int> ShowChoicePromptAsync(string message, string title, IReadOnlyList<string> buttonLabels)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return -1;

        var dialog = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#2d2d2d"))
        };

        var stackPanel = new StackPanel { Margin = new Thickness(20) };
        stackPanel.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 10, 0, 20),
            Foreground = Brushes.White,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };

        var result = -1;
        for (var i = 0; i < buttonLabels.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttonLabels[i],
                Height = 32,
                Padding = new Thickness(12, 0),
                Background = new SolidColorBrush(Color.Parse(i == 0 ? "#0d6efd" : "#3d3d3d")),
                Foreground = Brushes.White
            };
            button.Click += (_, _) => { result = index; dialog.Close(); };
            buttonPanel.Children.Add(button);
        }

        stackPanel.Children.Add(buttonPanel);
        dialog.Content = stackPanel;
        await dialog.ShowDialog(desktop.MainWindow);
        return result;
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build ConnectAPICPro.sln -v q --nologo`
Expected: 0 Fehler.

- [ ] **Step 4: Commit**

```bash
git add CAP.Avalonia/Services/IMessageBoxService.cs CAP.Avalonia/Services/MessageBoxService.cs
git commit -m "(+) MessageBoxService: generic multi-choice prompt (ShowChoicePromptAsync)"
```

---

### Task 2: Managed-Env-Kandidaten + Install-Angebot im `GdsExportViewModel`

**Files:**
- Create: `CAP.Avalonia/ViewModels/Export/ManagedEnvCandidate.cs`
- Modify: `CAP.Avalonia/ViewModels/Export/GdsExportViewModel.cs`
- Test: `UnitTests/Export/GdsExportEnvironmentSelectionTests.cs` (neu)

**Interfaces:**
- Produces (von Task 5 DI-verdrahtet, von Task 6 UI-gebunden):
  - `record ManagedEnvCandidate(string Name, string PythonExecutable, string DisplayText);`
  - `Func<IReadOnlyList<ManagedEnvCandidate>>? ManagedEnvironmentsProvider { get; set; }`
  - `Action<string>? ActivateManagedEnvironment { get; set; }`
  - `Action? RequestNazcaInstall { get; set; }`
  - `ObservableCollection<ManagedEnvCandidate> ManagedCandidates { get; }`
  - `bool ShowNazcaInstallOffer { get; }` (ObservableProperty)
  - `void RefreshManagedCandidates()`
  - `[RelayCommand] Task SelectManagedEnvironment(ManagedEnvCandidate candidate)` → generiert `SelectManagedEnvironmentCommand`
  - `[RelayCommand] void InstallNazca()` → generiert `InstallNazcaCommand`

- [ ] **Step 1: Failing Tests schreiben**

Neue Datei `UnitTests/Export/GdsExportEnvironmentSelectionTests.cs`:

```csharp
using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Export;

/// <summary>
/// Tests for the managed-environment candidate list and the "install Nazca"
/// offer on <see cref="GdsExportViewModel"/> (settings page integration).
/// </summary>
public class GdsExportEnvironmentSelectionTests
{
    private static GdsExportViewModel CreateViewModel() =>
        new(new GdsExportService());

    [Fact]
    public void RefreshManagedCandidates_WithProvider_ListsAllCandidates()
    {
        var vm = CreateViewModel();
        vm.ManagedEnvironmentsProvider = () => new[]
        {
            new ManagedEnvCandidate("nazca", "/envs/nazca/bin/python", "Managed · nazca"),
            new ManagedEnvCandidate("py312", "/envs/py312/bin/python", "Managed · py312"),
        };

        vm.RefreshManagedCandidates();

        vm.ManagedCandidates.Count.ShouldBe(2);
        vm.ManagedCandidates[0].Name.ShouldBe("nazca");
    }

    [Fact]
    public void RefreshManagedCandidates_NoNazcaAnywhere_ShowsInstallOffer()
    {
        var vm = CreateViewModel();
        vm.NazcaAvailable = false;
        vm.ManagedEnvironmentsProvider = () => Array.Empty<ManagedEnvCandidate>();

        vm.RefreshManagedCandidates();

        vm.ShowNazcaInstallOffer.ShouldBeTrue();
    }

    [Fact]
    public void RefreshManagedCandidates_NazcaInActiveInterpreter_HidesInstallOffer()
    {
        var vm = CreateViewModel();
        vm.NazcaAvailable = true;
        vm.ManagedEnvironmentsProvider = () => Array.Empty<ManagedEnvCandidate>();

        vm.RefreshManagedCandidates();

        vm.ShowNazcaInstallOffer.ShouldBeFalse();
    }

    [Fact]
    public void RefreshManagedCandidates_ManagedEnvExists_HidesInstallOfferEvenWithoutActiveNazca()
    {
        var vm = CreateViewModel();
        vm.NazcaAvailable = false;
        vm.ManagedEnvironmentsProvider = () => new[]
        {
            new ManagedEnvCandidate("nazca", "/envs/nazca/bin/python", "Managed · nazca"),
        };

        vm.RefreshManagedCandidates();

        vm.ShowNazcaInstallOffer.ShouldBeFalse();
    }

    [Fact]
    public void RefreshManagedCandidates_WithoutProvider_IsEmptyAndOffersInstallWhenNazcaMissing()
    {
        var vm = CreateViewModel();
        vm.NazcaAvailable = false;

        vm.RefreshManagedCandidates();

        vm.ManagedCandidates.ShouldBeEmpty();
        vm.ShowNazcaInstallOffer.ShouldBeTrue();
    }

    [Fact]
    public async Task SelectManagedEnvironment_InvokesActivationDelegate()
    {
        var vm = CreateViewModel();
        string? activated = null;
        vm.ActivateManagedEnvironment = name => activated = name;
        var candidate = new ManagedEnvCandidate("nazca", "/envs/nazca/bin/python", "Managed · nazca");

        await vm.SelectManagedEnvironmentCommand.ExecuteAsync(candidate);

        activated.ShouldBe("nazca");
    }

    [Fact]
    public void InstallNazca_InvokesRequestDelegate()
    {
        var vm = CreateViewModel();
        var requested = false;
        vm.RequestNazcaInstall = () => requested = true;

        vm.InstallNazcaCommand.Execute(null);

        requested.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: RED verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExportEnvironmentSelection`
Expected: Compile-Fehler (`ManagedEnvCandidate`, `RefreshManagedCandidates` etc. existieren nicht) — das ist das RED-Signal bei neuer API.

- [ ] **Step 3: `ManagedEnvCandidate` anlegen**

`CAP.Avalonia/ViewModels/Export/ManagedEnvCandidate.cs`:

```csharp
namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// A managed Python environment offered as an interpreter candidate on the
/// GDS-Export settings page. Deliberately a plain DTO: the GDS-export slice
/// never imports the environment-manager slice — the DI layer maps registry
/// entries into this type (packages beyond Nazca, e.g. gdsfactory, extend
/// <paramref name="DisplayText"/> later without UI changes).
/// </summary>
/// <param name="Name">Registry name of the environment (activation key).</param>
/// <param name="PythonExecutable">Full path to the environment's interpreter.</param>
/// <param name="DisplayText">Human-readable list entry, e.g. "Managed · nazca · Python 3.11 · Nazca 0.6.1".</param>
public sealed record ManagedEnvCandidate(string Name, string PythonExecutable, string DisplayText);
```

- [ ] **Step 4: `GdsExportViewModel` erweitern**

Zu den Feldern/Properties ergänzen:

```csharp
    [ObservableProperty]
    private ObservableCollection<ManagedEnvCandidate> _managedCandidates = new();

    [ObservableProperty]
    private bool _showNazcaInstallOffer;

    /// <summary>
    /// Supplies the managed environments that carry Nazca (wired by the DI layer;
    /// the GDS-export slice never references the environment-manager slice directly).
    /// </summary>
    public Func<IReadOnlyList<ManagedEnvCandidate>>? ManagedEnvironmentsProvider { get; set; }

    /// <summary>Activates a managed environment by name (wired by the DI layer).</summary>
    public Action<string>? ActivateManagedEnvironment { get; set; }

    /// <summary>Starts the default Nazca environment installation (wired by the DI layer).</summary>
    public Action? RequestNazcaInstall { get; set; }
```

Methoden ergänzen:

```csharp
    /// <summary>
    /// Rebuilds <see cref="ManagedCandidates"/> from <see cref="ManagedEnvironmentsProvider"/>
    /// and recomputes whether the "install Nazca" offer should be shown (no Nazca in the
    /// active interpreter AND no managed environment to switch to).
    /// </summary>
    public void RefreshManagedCandidates()
    {
        ManagedCandidates.Clear();
        foreach (var candidate in ManagedEnvironmentsProvider?.Invoke() ?? Array.Empty<ManagedEnvCandidate>())
            ManagedCandidates.Add(candidate);

        ShowNazcaInstallOffer = !NazcaAvailable && ManagedCandidates.Count == 0;
    }

    /// <summary>
    /// Activates a managed environment: the registry callback (DI layer) persists the
    /// interpreter and pushes it into this ViewModel, then the status is re-checked.
    /// </summary>
    [RelayCommand]
    public async Task SelectManagedEnvironment(ManagedEnvCandidate candidate)
    {
        ActivateManagedEnvironment?.Invoke(candidate.Name);
        await CheckEnvironmentAsync();
    }

    /// <summary>Requests creation + installation of the default Nazca environment.</summary>
    [RelayCommand]
    public void InstallNazca() => RequestNazcaInstall?.Invoke();
```

Am Ende von `CheckEnvironmentAsync` (im `try` nach `OnPropertyChanged(nameof(IsEnvironmentReady));` **und** im `catch` nach dem dortigen `OnPropertyChanged`) jeweils ergänzen:

```csharp
            RefreshManagedCandidates();
```

- [ ] **Step 5: GREEN verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExportEnvironmentSelection`
Expected: 7 passed.

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/ViewModels/Export/ManagedEnvCandidate.cs CAP.Avalonia/ViewModels/Export/GdsExportViewModel.cs UnitTests/Export/GdsExportEnvironmentSelectionTests.cs
git commit -m "(+) GDS settings: managed Nazca environments as selectable interpreter candidates"
```

---

### Task 3: GDS-Preflight-Entscheidung (`PreflightGdsAsync`)

**Files:**
- Modify: `CAP.Avalonia/ViewModels/Export/GdsExportViewModel.cs`
- Test: `UnitTests/Export/GdsExportEnvironmentSelectionTests.cs` (erweitern)

**Interfaces:**
- Produces (von Task 8 konsumiert):
  - `enum GdsPreflightDecision { Proceed, SkipGds, InstallRequested, OpenSettingsRequested }` (eigene Datei `CAP.Avalonia/ViewModels/Export/GdsPreflightDecision.cs`)
  - `Task<GdsPreflightDecision> PreflightGdsAsync(IMessageBoxService? messageBox)` — reine Entscheidung auf Basis des AKTUELLEN Zustands (`GenerateGdsEnabled`, `IsEnvironmentReady`); ruft selbst KEINEN Environment-Check auf (das macht der Aufrufer vorher), damit Tests deterministisch bleiben.
- Consumes: `IMessageBoxService.ShowChoicePromptAsync` aus Task 1.

- [ ] **Step 1: Failing Tests schreiben**

In `GdsExportEnvironmentSelectionTests.cs` ergänzen (plus `using CAP.Avalonia.Services;`):

```csharp
    private sealed class FakeMessageBoxService : IMessageBoxService
    {
        public int ChoiceToReturn { get; set; } = -1;
        public string? LastMessage { get; private set; }
        public IReadOnlyList<string>? LastButtons { get; private set; }

        public Task<SavePromptResult> ShowSavePromptAsync(string message, string title) =>
            Task.FromResult(SavePromptResult.Cancel);

        public Task<int> ShowChoicePromptAsync(string message, string title, IReadOnlyList<string> buttonLabels)
        {
            LastMessage = message;
            LastButtons = buttonLabels;
            return Task.FromResult(ChoiceToReturn);
        }
    }

    [Fact]
    public async Task PreflightGds_GdsGenerationDisabled_ProceedsWithoutDialog()
    {
        var vm = CreateViewModel();
        vm.GenerateGdsEnabled = false;
        var messageBox = new FakeMessageBoxService();

        var decision = await vm.PreflightGdsAsync(messageBox);

        decision.ShouldBe(GdsPreflightDecision.Proceed);
        messageBox.LastMessage.ShouldBeNull();
    }

    [Fact]
    public async Task PreflightGds_EnvironmentReady_ProceedsWithoutDialog()
    {
        var vm = CreateViewModel();
        vm.GenerateGdsEnabled = true;
        vm.PythonAvailable = true;
        vm.NazcaAvailable = true;
        var messageBox = new FakeMessageBoxService();

        var decision = await vm.PreflightGdsAsync(messageBox);

        decision.ShouldBe(GdsPreflightDecision.Proceed);
        messageBox.LastMessage.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, GdsPreflightDecision.InstallRequested)]
    [InlineData(1, GdsPreflightDecision.OpenSettingsRequested)]
    [InlineData(2, GdsPreflightDecision.SkipGds)]
    [InlineData(-1, GdsPreflightDecision.SkipGds)]   // Dialog geschlossen
    public async Task PreflightGds_NazcaMissing_MapsDialogChoice(int choice, GdsPreflightDecision expected)
    {
        var vm = CreateViewModel();
        vm.GenerateGdsEnabled = true;
        vm.PythonAvailable = true;
        vm.NazcaAvailable = false;
        var messageBox = new FakeMessageBoxService { ChoiceToReturn = choice };

        var decision = await vm.PreflightGdsAsync(messageBox);

        decision.ShouldBe(expected);
        messageBox.LastButtons!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task PreflightGds_NoMessageBoxService_ProceedsLikeBefore()
    {
        // Headless/legacy callers without a dialog service keep the old behavior:
        // attempt the GDS generation and surface the failure in the result.
        var vm = CreateViewModel();
        vm.GenerateGdsEnabled = true;
        vm.NazcaAvailable = false;

        var decision = await vm.PreflightGdsAsync(null);

        decision.ShouldBe(GdsPreflightDecision.Proceed);
    }
```

- [ ] **Step 2: RED verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExportEnvironmentSelection`
Expected: Compile-Fehler (`GdsPreflightDecision`/`PreflightGdsAsync` fehlen).

- [ ] **Step 3: Implementieren**

`CAP.Avalonia/ViewModels/Export/GdsPreflightDecision.cs`:

```csharp
namespace CAP.Avalonia.ViewModels.Export;

/// <summary>Outcome of the pre-flight check before generating a GDS file.</summary>
public enum GdsPreflightDecision
{
    /// <summary>Environment is ready (or GDS generation is disabled/headless) — continue.</summary>
    Proceed,

    /// <summary>User chose to skip the GDS step; the Nazca script export stands alone.</summary>
    SkipGds,

    /// <summary>User asked to install Nazca now (open settings + start default install).</summary>
    InstallRequested,

    /// <summary>User asked to open the settings without installing.</summary>
    OpenSettingsRequested,
}
```

In `GdsExportViewModel` (using `CAP.Avalonia.Services;` ergänzen):

```csharp
    /// <summary>
    /// Decides how to proceed with GDS generation based on the current environment
    /// state. Callers refresh the state (<see cref="CheckEnvironmentAsync"/>) first;
    /// this method only maps state + user choice to a decision.
    /// </summary>
    /// <param name="messageBox">Dialog service; null (headless) proceeds like before.</param>
    public async Task<GdsPreflightDecision> PreflightGdsAsync(IMessageBoxService? messageBox)
    {
        if (!GenerateGdsEnabled || IsEnvironmentReady || messageBox == null)
            return GdsPreflightDecision.Proceed;

        var choice = await messageBox.ShowChoicePromptAsync(
            "Nazca is required to generate a GDS file, but no Python environment with Nazca is available. "
            + "The Nazca Python script itself has been exported.",
            "Nazca required",
            new[] { "Install Nazca now", "Open Settings", "Skip GDS" });

        return choice switch
        {
            0 => GdsPreflightDecision.InstallRequested,
            1 => GdsPreflightDecision.OpenSettingsRequested,
            _ => GdsPreflightDecision.SkipGds,
        };
    }
```

- [ ] **Step 4: GREEN verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExportEnvironmentSelection`
Expected: alle Tests passed (7 aus Task 2 + 7 neue).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/ViewModels/Export/GdsPreflightDecision.cs CAP.Avalonia/ViewModels/Export/GdsExportViewModel.cs UnitTests/Export/GdsExportEnvironmentSelectionTests.cs
git commit -m "(+) GDS export: pre-flight decision when Nazca is missing (install/settings/skip)"
```

---

### Task 4: `StartDefaultNazcaInstallAsync` im `PythonEnvironmentManagerViewModel`

**Files:**
- Modify: `CAP.Avalonia/ViewModels/Export/PythonEnvironmentManager/PythonEnvironmentManagerViewModel.cs`
- Test: `UnitTests/Export/PythonEnvironmentManager/PythonEnvironmentManagerViewModelTests.cs` (erweitern)

**Interfaces:**
- Produces (von Task 5 DI-verdrahtet): `Task StartDefaultNazcaInstallAsync()` und `public const string DefaultEnvironmentName = "nazca";`
- Consumes: bestehendes `CreateAndInstallCommand`, `PythonEnvironmentRegistry.Exists(string)`, `UvBootstrapper.DefaultPythonVersion`.

- [ ] **Step 1: Failing Tests schreiben**

In `PythonEnvironmentManagerViewModelTests.cs` ergänzen:

```csharp
    [Fact]
    public async Task StartDefaultNazcaInstall_EnvAlreadyExists_DoesNotCreateDuplicateAndExplains()
    {
        var registry = CreateRegistry();
        registry.AddOrUpdate(new PythonEnvironment
        {
            Name = PythonEnvironmentManagerViewModel.DefaultEnvironmentName,
            VenvPath = Path.Combine(UvBootstrapper.EnvironmentsBaseDir,
                PythonEnvironmentManagerViewModel.DefaultEnvironmentName),
        });
        var vm = CreateViewModel(registry);

        await vm.StartDefaultNazcaInstallAsync();

        registry.GetAll().Count.ShouldBe(1);                 // kein Duplikat
        vm.IsBusy.ShouldBeFalse();                           // kein Install gestartet
        vm.ProgressText.ShouldContain(
            PythonEnvironmentManagerViewModel.DefaultEnvironmentName);
    }

    [Fact]
    public async Task StartDefaultNazcaInstall_WhileBusy_IsIgnored()
    {
        var registry = CreateRegistry();
        var vm = CreateViewModel(registry);
        vm.IsBusy = true;

        await vm.StartDefaultNazcaInstallAsync();

        registry.GetAll().ShouldBeEmpty();                   // nichts registriert
    }
```

- [ ] **Step 2: RED verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" PythonEnvironmentManagerViewModel`
Expected: Compile-Fehler (`DefaultEnvironmentName`, `StartDefaultNazcaInstallAsync` fehlen).

- [ ] **Step 3: Implementieren**

Im ViewModel ergänzen:

```csharp
    /// <summary>Name of the environment created by the one-click "install Nazca" offers.</summary>
    public const string DefaultEnvironmentName = "nazca";

    /// <summary>
    /// One-click entry point used by the GDS-export fallback and the export guard:
    /// creates the default Nazca environment with default settings. No-ops (with an
    /// explanatory status) when an environment of that name already exists, and does
    /// nothing while another operation runs.
    /// </summary>
    public async Task StartDefaultNazcaInstallAsync()
    {
        if (IsBusy) return;

        if (_registry.Exists(DefaultEnvironmentName))
        {
            ProgressText = $"Environment '{DefaultEnvironmentName}' already exists — "
                + "select it below or use Repair if it is broken.";
            return;
        }

        NewEnvironmentName = DefaultEnvironmentName;
        PythonVersion = UvBootstrapper.DefaultPythonVersion;
        await CreateAndInstallCommand.ExecuteAsync(null);
    }
```

- [ ] **Step 4: GREEN verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" PythonEnvironmentManagerViewModel`
Expected: alle passed (bestehende + 2 neue).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/ViewModels/Export/PythonEnvironmentManager/PythonEnvironmentManagerViewModel.cs UnitTests/Export/PythonEnvironmentManager/PythonEnvironmentManagerViewModelTests.cs
git commit -m "(+) Env manager: one-click default Nazca install entry point"
```

---

