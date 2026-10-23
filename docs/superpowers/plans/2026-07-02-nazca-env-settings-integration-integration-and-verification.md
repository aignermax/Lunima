# Composition, interface and verification

> Historical July 2026 implementation plan. [Plan index](2026-07-02-nazca-env-settings-integration.md).
> For current design rules, see [Architecture](../../../ARCHITECTURE.md) and [Codebase map](../../CODEBASE_MAP.md).

### Task 5: DI-Verdrahtung der Delegates

**Files:**
- Modify: `CAP.Avalonia/DI/ExportFeatureExtensions.cs`

**Interfaces:**
- Consumes: Task 2 (`ManagedEnvironmentsProvider`, `ActivateManagedEnvironment`, `RequestNazcaInstall`, `ManagedEnvCandidate`), Task 4 (`StartDefaultNazcaInstallAsync`), `PythonEnvironmentRegistry` (`GetAll()`, `SetActive(string)`, `PythonEnvironment.IsHealthy/PythonExecutable/PythonVersion/NazcaVersion`).
- DI-Glue darf beide Slices importieren (wie `PythonEnvFeatureExtensions` bereits tut); Auflösung IMMER lazy im Delegate (`sp.GetRequiredService<...>` erst beim Aufruf), damit keine Zirkularität bei der Container-Konstruktion entsteht.

Kein eigener Unit-Test (Glue); die Delegate-Logik ist in Task 2/4 getestet, die Verdrahtung wird in Task 9 im laufenden Programm verifiziert.

- [ ] **Step 1: Verdrahtung in der `GdsExportViewModel`-Factory ergänzen**

In `ExportFeatureExtensions.AddExportFeature`, innerhalb der bestehenden Factory nach `vm.OnPythonPathChanged = ...` (usings ergänzen: `using CAP_Core.Export.PythonEnvironmentManager;` und `using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;`):

```csharp
            // Managed-environment integration (lazy resolution — the registry and the
            // env-manager ViewModel are resolved when the delegate fires, not at build time).
            vm.ManagedEnvironmentsProvider = () =>
                sp.GetRequiredService<PythonEnvironmentRegistry>().GetAll()
                    .Where(e => e.IsHealthy)
                    .Select(e => new ManagedEnvCandidate(
                        e.Name,
                        e.PythonExecutable,
                        $"Managed · {e.Name} · Python {e.PythonVersion ?? "?"} · Nazca {e.NazcaVersion ?? "?"}"))
                    .ToList();
            vm.ActivateManagedEnvironment = name =>
                sp.GetRequiredService<PythonEnvironmentRegistry>().SetActive(name);
            vm.RequestNazcaInstall = () =>
                _ = sp.GetRequiredService<PythonEnvironmentManagerViewModel>()
                    .StartDefaultNazcaInstallAsync();
```

- [ ] **Step 2: Build + bestehende Tests**

Run: `dotnet build ConnectAPICPro.sln -v q --nologo` und
`$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExport`
Expected: Build 0 Fehler, Tests grün.

- [ ] **Step 3: Commit**

```bash
git add CAP.Avalonia/DI/ExportFeatureExtensions.cs
git commit -m "(+) DI: wire managed-env candidates + Nazca-install request into GdsExportViewModel"
```

---

### Task 6: Settings-Seite „Python Environments" + Navigation + GDS-Seiten-UI

**Files:**
- Create: `CAP.Avalonia/ViewModels/Settings/PythonEnvironmentsSettingsPage.cs`
- Modify: `CAP.Avalonia/DI/SettingsFeatureExtensions.cs`
- Modify: `CAP.Avalonia/ViewModels/Settings/SettingsWindowViewModel.cs`
- Modify: `CAP.Avalonia/Views/SettingsWindow.axaml`
- Modify: `CAP.Avalonia/Views/SettingsWindow.axaml.cs`
- Test: `UnitTests/ViewModels/SettingsWindowViewModelTests.cs` (falls nicht vorhanden: neu)

**Interfaces:**
- Produces: `PythonEnvironmentsSettingsPage : ISettingsPage` (Title „Python Environments", Icon „📦", Category „Export", ViewModel = `PythonEnvironmentManagerViewModel`); `SettingsWindowViewModel.SelectPage(Type pageType)`.
- Consumes: `ISettingsPage`, `PythonEnvironmentManagerViewModel`, `GdsExportViewModel.InstallNazcaCommand` + `ManagedCandidates` + `ShowNazcaInstallOffer` + `SelectManagedEnvironmentCommand` (Task 2).

- [ ] **Step 1: Failing Test für `SelectPage`**

`UnitTests/ViewModels/SettingsWindowViewModelTests.cs` (neu, falls fehlend):

```csharp
using CAP.Avalonia.ViewModels.Settings;
using Shouldly;

namespace UnitTests.ViewModels;

/// <summary>Tests for settings-page navigation by page type.</summary>
public class SettingsWindowViewModelTests
{
    private sealed class PageA : ISettingsPage
    {
        public string Title => "A";
        public string Icon => "a";
        public string? Category => null;
        public object ViewModel => this;
    }

    private sealed class PageB : ISettingsPage
    {
        public string Title => "B";
        public string Icon => "b";
        public string? Category => null;
        public object ViewModel => this;
    }

    [Fact]
    public void SelectPage_KnownType_SwitchesSelectedPage()
    {
        var vm = new SettingsWindowViewModel(new ISettingsPage[] { new PageA(), new PageB() });

        vm.SelectPage(typeof(PageB));

        vm.SelectedPage.ShouldBeOfType<PageB>();
    }

    [Fact]
    public void SelectPage_UnknownType_KeepsCurrentSelection()
    {
        var vm = new SettingsWindowViewModel(new ISettingsPage[] { new PageA() });

        vm.SelectPage(typeof(PageB));

        vm.SelectedPage.ShouldBeOfType<PageA>();
    }
}
```

- [ ] **Step 2: RED verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SettingsWindowViewModel`
Expected: Compile-Fehler (`SelectPage` fehlt).

- [ ] **Step 3: `SelectPage` implementieren**

In `SettingsWindowViewModel`:

```csharp
    /// <summary>
    /// Selects the page whose runtime type matches <paramref name="pageType"/>;
    /// keeps the current selection when no such page is registered.
    /// </summary>
    public void SelectPage(Type pageType)
    {
        var page = Pages.FirstOrDefault(p => p.GetType() == pageType);
        if (page != null)
            SelectedPage = page;
    }
```

- [ ] **Step 4: GREEN verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SettingsWindowViewModel`
Expected: 2 passed.

- [ ] **Step 5: Settings-Seite anlegen + registrieren**

`CAP.Avalonia/ViewModels/Settings/PythonEnvironmentsSettingsPage.cs`:

```csharp
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;

namespace CAP.Avalonia.ViewModels.Settings;

/// <summary>
/// Settings page hosting the managed Python environment manager (create, install
/// Nazca, health-check, repair, remove, set active). Lives in Settings — not the
/// Properties sidebar — because environments are application-wide configuration.
/// </summary>
public class PythonEnvironmentsSettingsPage : ISettingsPage
{
    /// <inheritdoc/>
    public string Title => "Python Environments";

    /// <inheritdoc/>
    public string Icon => "📦";

    /// <inheritdoc/>
    public string? Category => "Export";

    /// <inheritdoc/>
    public object ViewModel { get; }

    /// <summary>Initializes a new instance of <see cref="PythonEnvironmentsSettingsPage"/>.</summary>
    public PythonEnvironmentsSettingsPage(PythonEnvironmentManagerViewModel viewModel)
    {
        ViewModel = viewModel;
    }
}
```

In `SettingsFeatureExtensions` nach der `GdsExportSettingsPage`-Zeile:

```csharp
        services.AddTransient<ISettingsPage, PythonEnvironmentsSettingsPage>();
```

- [ ] **Step 6: DataTemplate + GDS-Seiten-UI in `SettingsWindow.axaml`**

Namespace-Deklarationen am Window-Element ergänzen (falls nicht vorhanden):

```xml
xmlns:panels="using:CAP.Avalonia.Views.Panels"
xmlns:vmPyEnv="using:CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager"
```

Neues DataTemplate neben den bestehenden (das UserControl erbt den DataContext):

```xml
                    <!-- Python Environments (managed venvs with Nazca) -->
                    <DataTemplate DataType="{x:Type vmPyEnv:PythonEnvironmentManagerViewModel}">
                        <panels:PythonEnvironmentManagerPanel/>
                    </DataTemplate>
```

Im GDS-Export-DataTemplate nach dem bestehenden `AvailablePythons`-ItemsControl ergänzen:

```xml
                            <!-- Managed environments with Nazca (from the environment manager) -->
                            <ItemsControl ItemsSource="{Binding ManagedCandidates}"
                                          Margin="0,6,0,0"
                                          x:Name="ManagedCandidatesList"
                                          IsVisible="{Binding ManagedCandidates.Count}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Button Content="{Binding DisplayText}"
                                                Command="{Binding #ManagedCandidatesList.DataContext.SelectManagedEnvironmentCommand}"
                                                CommandParameter="{Binding}"
                                                HorizontalAlignment="Stretch"
                                                HorizontalContentAlignment="Left"
                                                Margin="0,2,0,0" Padding="6,4"
                                                Background="#2d3d2d" FontSize="11"/>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>

                            <!-- Fallback: no Nazca anywhere -->
                            <Border IsVisible="{Binding ShowNazcaInstallOffer}"
                                    Background="#3d3320" CornerRadius="4" Padding="10" Margin="0,10,0,0">
                                <StackPanel Spacing="6">
                                    <TextBlock Text="No Nazca environment found."
                                               Foreground="#e8c872" FontWeight="SemiBold" FontSize="12"/>
                                    <TextBlock Foreground="Gray" FontSize="11" TextWrapping="Wrap"
                                               Text="Lunima can create a managed Python environment and install Nazca into it automatically."/>
                                    <Button Content="Create + install Nazca now"
                                            Click="OnCreateNazcaEnvironmentClick"
                                            Background="#3d5d3d" HorizontalAlignment="Left"/>
                                </StackPanel>
                            </Border>
```

- [ ] **Step 7: Click-Handler in `SettingsWindow.axaml.cs`**

```csharp
    /// <summary>
    /// "Create + install Nazca now" on the GDS-Export page: starts the default
    /// install (via the GdsExportViewModel delegate) and navigates to the
    /// Python-Environments page so the progress is visible there.
    /// </summary>
    private void OnCreateNazcaEnvironmentClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as global::Avalonia.Controls.Button)?.DataContext
            is ViewModels.Export.GdsExportViewModel gdsExport)
            gdsExport.InstallNazcaCommand.Execute(null);

        if (DataContext is ViewModels.Settings.SettingsWindowViewModel vm)
            vm.SelectPage(typeof(ViewModels.Settings.PythonEnvironmentsSettingsPage));
    }
```

- [ ] **Step 8: Build + Tests**

Run: `dotnet build ConnectAPICPro.sln -v q --nologo` und
`$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" Settings`
Expected: Build 0 Fehler, Tests grün.

- [ ] **Step 9: Commit**

```bash
git add CAP.Avalonia/ViewModels/Settings/PythonEnvironmentsSettingsPage.cs CAP.Avalonia/DI/SettingsFeatureExtensions.cs CAP.Avalonia/ViewModels/Settings/SettingsWindowViewModel.cs CAP.Avalonia/Views/SettingsWindow.axaml CAP.Avalonia/Views/SettingsWindow.axaml.cs UnitTests/ViewModels/SettingsWindowViewModelTests.cs
git commit -m "(+) Settings: dedicated Python Environments page + managed candidates & Nazca-install offer on GDS page"
```

---

### Task 7: Env-Manager-Panel aus der Properties-Sidebar entfernen

**Files:**
- Modify: `CAP.Avalonia/Views/MainWindow.axaml` (Bereich um Zeile 736)
- Modify: `CAP.Avalonia/ViewModels/Panels/RightPanelViewModel.cs` (Property Zeile ~120, Ctor-Param Zeile ~159)
- Modify: `UnitTests/Helpers/MainViewModelTestHelper.cs` (Zeile ~154: die vier Env-Manager-Argumente entfernen)
- Modify: `UnitTests/ViewModels/PanelWidthPersistenceTests.cs` (Zeile ~85: dito)

**Interfaces:**
- `RightPanelViewModel` verliert `PythonEnvironmentManagerViewModel PythonEnvManager { get; }` und den zugehörigen Konstruktor-Parameter. Der DI-Container registriert das ViewModel weiterhin (Settings-Seite konsumiert es).

- [ ] **Step 1: XAML-Element entfernen**

In `MainWindow.axaml` die zwei Zeilen löschen:

```xml
                    <!-- Python Environment Manager Panel -->
                    <panels:PythonEnvironmentManagerPanel/>
```

- [ ] **Step 2: `RightPanelViewModel` bereinigen**

Property `public PythonEnvironmentManagerViewModel PythonEnvManager { get; }`, den Konstruktor-Parameter `PythonEnvironmentManagerViewModel pythonEnvManager` und die Zuweisung entfernen; ggf. jetzt ungenutzte `using`-Zeile mitnehmen.

- [ ] **Step 3: Testhelfer anpassen**

In `MainViewModelTestHelper.cs` und `PanelWidthPersistenceTests.cs` die vier Argumente
(`new PythonEnvironmentRegistry(...)`, `new UvBootstrapper()`, `new NazcaPackageInstaller()`, `new EnvironmentHealthChecker(new PythonDiscoveryService())`) aus der `RightPanelViewModel`-Konstruktion entfernen (sie fütterten nur den entfallenen Parameter).

- [ ] **Step 4: Build + volle Suite**

Run: `dotnet build ConnectAPICPro.sln -v q --nologo` und
`$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py"`
Expected: Build 0 Fehler, Suite grün (das Panel hängt jetzt nur noch an der Settings-Seite).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Views/MainWindow.axaml CAP.Avalonia/ViewModels/Panels/RightPanelViewModel.cs UnitTests/Helpers/MainViewModelTestHelper.cs UnitTests/ViewModels/PanelWidthPersistenceTests.cs
git commit -m "(-) Properties sidebar: remove Python Environment Manager panel (now a Settings page)"
```

---

### Task 8: Export-Guard in `FileOperationsViewModel.ExportNazca`

**Files:**
- Modify: `CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs` (Methode `ExportNazca`, Zeile ~1252; neue Property bei den anderen Service-Properties, Zeile ~113)
- Modify: `CAP.Avalonia/ViewModels/MainViewModel.cs` (Verdrahtung, dort wo `FileOperations`-Delegates gesetzt werden)
- Test: `UnitTests/Export/GdsExportGuardTests.cs` (neu)

**Interfaces:**
- Produces: `FileOperationsViewModel.ShowSettingsWindow` (`Func<Type?, Task>?`), von `MainViewModel` an `ShowSettingsWindowAsync` weitergereicht.
- Consumes: `GdsExport.PreflightGdsAsync(IMessageBoxService?)` + `GdsPreflightDecision` (Task 3), `GdsExport.InstallNazcaCommand` (Task 2), `MessageBoxService`-Property (existiert, Zeile ~113), `PythonEnvironmentsSettingsPage` (Task 6).

- [ ] **Step 1: Failing Test schreiben**

`UnitTests/Export/GdsExportGuardTests.cs` — nutzt den bestehenden `MainViewModelTestHelper` (liefert einen voll verdrahteten `MainViewModel`; Muster siehe `UnitTests/ViewModels/PanelWidthPersistenceTests.cs`) und `TestComponentFactory` für eine platzierbare Komponente:

```csharp
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Export;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.Export;

/// <summary>
/// Export-guard tests: triggering the Nazca export with GDS generation enabled but
/// no Nazca available must prompt instead of silently failing the GDS step.
/// </summary>
public class GdsExportGuardTests
{
    private sealed class RecordingMessageBox : IMessageBoxService
    {
        public int ChoiceToReturn { get; set; } = 2; // "Skip GDS"
        public int Calls { get; private set; }

        public Task<SavePromptResult> ShowSavePromptAsync(string message, string title) =>
            Task.FromResult(SavePromptResult.Cancel);

        public Task<int> ShowChoicePromptAsync(string message, string title, IReadOnlyList<string> buttonLabels)
        {
            Calls++;
            return Task.FromResult(ChoiceToReturn);
        }
    }

    private sealed class FixedPathFileDialog : IFileDialogService
    {
        private readonly string _path;
        public FixedPathFileDialog(string path) => _path = path;
        public Task<string?> ShowSaveFileDialogAsync(string title, string ext, string filter) =>
            Task.FromResult<string?>(_path);
        public Task<string?> ShowOpenFileDialogAsync(string title, string filter) =>
            Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task ExportNazca_GdsEnabledButNazcaMissing_PromptsAndSkipsGds()
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"lunima-export-{Guid.NewGuid():N}.py");
        try
        {
            var main = MainViewModelTestHelper.Create();
            var fileOps = main.FileOperations;
            var messageBox = new RecordingMessageBox { ChoiceToReturn = 2 };
            fileOps.MessageBoxService = messageBox;
            fileOps.FileDialogService = new FixedPathFileDialog(scriptPath);
            string? lastStatus = null;
            fileOps.UpdateStatus = s => lastStatus = s;

            // Umgebung als "kein Nazca" markieren, GDS-Generierung aktiv:
            fileOps.GdsExport.GenerateGdsEnabled = true;
            fileOps.GdsExport.PythonAvailable = true;
            fileOps.GdsExport.NazcaAvailable = false;

            // Eine Komponente, damit der Export nicht am leeren Canvas abbricht:
            MainViewModelTestHelper.PlaceAnyComponent(main);

            await fileOps.ExportNazcaCommand.ExecuteAsync(null);

            messageBox.Calls.ShouldBe(1);                    // Guard hat gefragt
            File.Exists(scriptPath).ShouldBeTrue();          // Skript wurde trotzdem exportiert
            lastStatus.ShouldNotBeNull();
            lastStatus!.ShouldContain("GDS skipped");
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }
}
```

Hinweis für den Implementierer: Die exakten Helfernamen (`MainViewModelTestHelper.Create()`, `PlaceAnyComponent`, `IFileDialogService`-Signaturen, `UpdateStatus`-Delegat) VOR dem Schreiben an `UnitTests/Helpers/MainViewModelTestHelper.cs`, `UnitTests/ViewModels/PanelWidthPersistenceTests.cs` und `CAP.Avalonia/Services/FileDialogService.cs` abgleichen und den Test entsprechend anpassen — die Struktur oben ist verbindlich (Fake-Dialog zählt Aufrufe, Skript entsteht, Status enthält „GDS skipped"), die Helper-Aufrufe folgen dem Bestand. Existiert kein Platzierungs-Helfer, eine Komponente über `TestComponentFactory` + `_canvas`-Zugriff des Helpers platzieren.

- [ ] **Step 2: RED verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExportGuard`
Expected: FAIL — `messageBox.Calls` ist 0 und der Status meldet „GDS generation failed" statt „GDS skipped" (der Guard existiert noch nicht).

- [ ] **Step 3: Guard implementieren**

In `FileOperationsViewModel` bei den Service-Properties (~Zeile 113) ergänzen:

```csharp
    /// <summary>
    /// Opens the Settings window, optionally pre-selecting a page by type.
    /// Wired by <see cref="MainViewModel"/>; null in headless contexts.
    /// </summary>
    public Func<Type?, Task>? ShowSettingsWindow { get; set; }
```

In `ExportNazca()` den Block zwischen Skript-Schreiben und GDS-Generierung ersetzen:

```csharp
                // Export Python script
                var nazcaCode = _nazcaExporter.Export(_canvas, overrides: StoredNazcaOverrides);
                await File.WriteAllTextAsync(filePath, nazcaCode);

                // GDS pre-flight: refresh a stale "not ready" verdict once, then ask the
                // user how to proceed when Nazca is genuinely unavailable.
                if (GdsExport.GenerateGdsEnabled && !GdsExport.IsEnvironmentReady)
                    await GdsExport.CheckEnvironmentAsync();

                var decision = await GdsExport.PreflightGdsAsync(MessageBoxService);
                if (decision == GdsPreflightDecision.InstallRequested)
                {
                    GdsExport.InstallNazcaCommand.Execute(null);
                    if (ShowSettingsWindow != null)
                        await ShowSettingsWindow(typeof(Settings.PythonEnvironmentsSettingsPage));
                    UpdateStatus?.Invoke($"Exported {Path.GetFileName(filePath)} — GDS skipped (installing Nazca)");
                    return;
                }
                if (decision == GdsPreflightDecision.OpenSettingsRequested)
                {
                    if (ShowSettingsWindow != null)
                        await ShowSettingsWindow(typeof(Settings.PythonEnvironmentsSettingsPage));
                    UpdateStatus?.Invoke($"Exported {Path.GetFileName(filePath)} — GDS skipped (Nazca not available)");
                    return;
                }
                if (decision == GdsPreflightDecision.SkipGds)
                {
                    UpdateStatus?.Invoke($"Exported {Path.GetFileName(filePath)} — GDS skipped (Nazca not available)");
                    return;
                }

                // Attempt GDS generation if enabled
                var result = await GdsExport.ExportScriptToGdsAsync(filePath);
```

(Usings ergänzen: `using CAP.Avalonia.ViewModels.Export;` und `using CAP.Avalonia.ViewModels.Settings;` — der `Settings.`-Qualifier im Code oben entsprechend anpassen.)

**Achtung Determinismus im Test:** `CheckEnvironmentAsync` würde den manipulierten Zustand überschreiben. Der Aufruf ist bewusst mit `!IsEnvironmentReady` geguardet — im Test ist `PythonAvailable=true, NazcaAvailable=false` → nicht ready → Check läuft und findet auf Maschinen MIT systemweitem Nazca doch eine Umgebung. Damit der Test überall deterministisch ist, VOR dem Check zusätzlich `GdsExport.CustomPythonPath` im Test auf einen nicht existierenden Pfad setzen (`Path.Combine(Path.GetTempPath(), "no-python-here", "python")` via `SetPythonPathAsync` NICHT verwenden — direkt `fileOps.GdsExport.CustomPythonPath = ...;` reicht nicht, weil der Service den Pfad hält). Deshalb im Test stattdessen: `await fileOps.GdsExport.SetPythonPathAsync(bogusPath);` VOR dem Setzen von `NazcaAvailable=false`. Dann findet der Re-Check keinen Interpreter und die Not-ready-Diagnose bleibt stabil.

- [ ] **Step 4: `MainViewModel` verdrahten**

An der Stelle, an der `FileOperations`-Delegates gesetzt werden (Konstruktor, in der Nähe von `UpdateStatus`-Wiring):

```csharp
        FileOperations.ShowSettingsWindow = async pageType =>
        {
            if (ShowSettingsWindowAsync != null)
                await ShowSettingsWindowAsync(pageType);
        };
```

- [ ] **Step 5: GREEN verifizieren**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsExportGuard`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs CAP.Avalonia/ViewModels/MainViewModel.cs UnitTests/Export/GdsExportGuardTests.cs
git commit -m "(+) Export guard: prompt to install Nazca when GDS generation has no environment"
```

---

### Task 9: Gesamtverifikation

**Files:** keine neuen.

- [ ] **Step 1: Volle Suite**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py"`
Expected: 0 failed.

- [ ] **Step 2: Build ohne neue Warnungen**

Run: `dotnet build ConnectAPICPro.sln -v q --nologo`
Expected: 0 Fehler; keine NEUEN Warnungen gegenüber main.

- [ ] **Step 3: UI-Verifikation (Screenshot-Harness)**

Lunima besitzt einen UI-Screenshot-Harness (siehe PR #572). App starten, verifizieren:
1. Properties-Sidebar enthält KEIN „Python Environment"-Panel mehr.
2. Settings → „Python Environments" zeigt das Manager-Panel.
3. Settings → „GDS Export" → „Check Environment" listet verwaltete Envs (falls vorhanden) bzw. zeigt das Banner „No Nazca environment found." (zum Provozieren: Python-Pfad auf Unsinn setzen).
4. Banner-Button wechselt auf die Env-Seite.
Screenshots der vier Zustände zur PR-Beschreibung legen.

- [ ] **Step 4: PR erstellen**

```bash
git push -u origin feat/nazca-env-settings-integration
gh pr create --title "(+) Settings: Python-Environments-Seite, Nazca-Kandidaten im GDS-Export + Install-Guard beim Export" --body "<Zusammenfassung gemäß Spec + Screenshots>"
```

---

## Self-Review (erledigt)

- **Spec-Abdeckung:** Sidebar-Entfernung (T7), eigene Settings-Seite (T6), Kandidatenliste + Aktivierung (T2/T5/T6), Fallback-Banner (T2/T6), Export-Guard (T1/T3/T8), Erweiterbarkeit gdsfactory (DTO-DisplayText, T2). ✓
- **Platzhalter:** Task 8 Step 1 verweist bewusst auf Bestands-Helfer mit verbindlicher Teststruktur; alle übrigen Steps tragen vollständigen Code. ✓
- **Typkonsistenz:** `ManagedEnvCandidate(Name, PythonExecutable, DisplayText)`, `GdsPreflightDecision`, `ShowChoicePromptAsync(message, title, IReadOnlyList<string>)`, `SelectPage(Type)`, `StartDefaultNazcaInstallAsync()`, `DefaultEnvironmentName` überall gleich benannt. ✓
