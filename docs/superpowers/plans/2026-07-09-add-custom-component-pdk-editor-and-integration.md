# Editor orchestration and integration

> Historical July 2026 implementation plan. [Plan index](2026-07-09-add-custom-component-pdk.md).
> For current design rules, see [Architecture](../../../ARCHITECTURE.md) and [Codebase map](../../CODEBASE_MAP.md).

### Task 5: `NewComponentViewModel` (Orchestrierung)

**Files:**
- Create: `CAP.Avalonia/ViewModels/Components/AddCustomComponent/NewComponentViewModel.cs`
- Test: `UnitTests/Components/AddCustomComponent/NewComponentViewModelTests.cs`

**Interfaces:**
- Consumes: `ComponentGeometryExtractor.ExtractAsync`; `IFdtdSMatrixService.CheckAvailabilityAsync`/`SolveAsync`; `ComponentFdtdRequestFactory.BuildFromPreview` (Task 3); `FdtdSMatrixConverter.ToComponentSMatrixData` (static); `FdtdSMatrixToDraftConverter` (Task 2); `UserPdkStore.Save`/`ComponentExists` (Task 1); DTOs `PdkComponentDraft`, `ProcessDefinition`, `PhysicalPinDraft`; `OverridePinData`.
- Produces:
  - `NewComponentViewModel(ComponentGeometryExtractor extractor, IFdtdSMatrixService? fdtd, UserPdkStore store, IReadOnlyList<ProcessDefinition> processes)`
  - `[ObservableProperty]` : `ComponentName`, `SelectedBackend` (GeometryBackend), `Module`, `Function`, `Parameters`, `SelectedProcess` (ProcessDefinition?), `StatusText`, `IsBusy`, `HasPreview`.
  - `[RelayCommand] RunPreview`, `[RelayCommand] ComputeSMatrix`, `[RelayCommand] Save`.
  - `PdkComponentDraft? SavedDraft { get; }`, `string? SavedProcessName { get; }`, `event EventHandler? Saved;` — damit die LeftPanel-Integration (Task 6) das Ergebnis registriert. `Func<string, string, Task<bool>>? ConfirmOverwrite` für Kollisionsdialog.

**Design notes (verhalten):**
- `RunPreview`: ruft `ExtractAsync`; bei Erfolg setzt `HasPreview=true`, merkt sich `GeometryExtractResult`.
- `ComputeSMatrix`: nur wenn `HasPreview` und `SelectedProcess != null`. Prüft `fdtd.CheckAvailabilityAsync`; baut `BuildFromPreview(raw, pins.Select(p=>p.Name))`; `SolveAsync`; bei `!Success` → `StatusText = Fehler`, KEINE S-Matrix (kein Save-taugliches Modell). Bei Erfolg → `ComponentSMatrixData` via `ToComponentSMatrixData`, gemerkt als `_computedModel`.
- `Save`: erfordert Name + Prozess + Preview. Baut `PdkComponentDraft` (Name, `GdsFactoryFunction`=QualifiedFunction wenn gdsfactory, sonst `NazcaFunction`; `WidthMicrometers`/`HeightMicrometers`; `Pins` aus `OverridePinData` → `PhysicalPinDraft`; `SMatrix` = `_computedModel==null ? BlackBox() : FromFdtd(_computedModel)`). Kollision via `ConfirmOverwrite`. Ruft `store.Save(...)`, setzt `SavedDraft`/`SavedProcessName`, feuert `Saved`.

- [ ] **Step 1: Read the real signatures**

Lies `CAP.Avalonia/Services/Solvers/FdtdSMatrixConverter.cs`, `CAP_Core/Solvers/Fdtd/IFdtdSMatrixService.cs` + `FdtdAvailability`, und `OverridePinMapper.cs` (`OverridePinData`-Member: `Name`, Offsets, Angle, `LogicalPinNumber`, `PinKind`?). Lies `PhysicalPinDraft.cs` für die Mapping-Zielfelder.

- [ ] **Step 2: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP_Core.Export;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Components.AddCustomComponent;

public class NewComponentViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lunima-nc-vm-" + Guid.NewGuid().ToString("N"));

    private static NazcaPreviewResult Ok() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 2,
        Polygons = new List<NazcaPreviewPolygon> { new() { Layer = 1, Vertices = new List<(double,double)> { (0,0),(10,0),(10,2),(0,2) } } },
        Pins = new List<NazcaPreviewPin> { new() { Name = "o1", X = 0, Y = 1, Angle = 180 }, new() { Name = "o2", X = 10, Y = 1, Angle = 0 } }
    };

    private (NewComponentViewModel vm, Mock<IFdtdSMatrixService> fdtd) Build(bool withFdtd = true)
    {
        var nazca = new Mock<NazcaComponentPreviewService>("python", "s.py", null, null);
        var gds = new Mock<GdsFactoryComponentPreviewService>("python", "g.py", null, null);
        gds.Setup(g => g.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Ok());
        var extractor = new ComponentGeometryExtractor(nazca.Object, gds.Object);
        var fdtd = new Mock<IFdtdSMatrixService>();
        var store = new UserPdkStore(_root, new PdkJsonSaver(), new PdkLoader());
        var vm = new NewComponentViewModel(extractor, withFdtd ? fdtd.Object : null, store,
            new List<ProcessDefinition> { new() { Name = "P" } });
        vm.ComponentName = "My Comp";
        vm.SelectedBackend = GeometryBackend.GdsFactory;
        vm.Module = "cspdk.sin300"; vm.Function = "coupler";
        vm.SelectedProcess = vm.Processes[0];
        return (vm, fdtd);
    }

    [Fact]
    public async Task Save_without_fdtd_writes_a_black_box_component()
    {
        var (vm, _) = Build(withFdtd: false);
        await vm.RunPreviewCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);

        vm.SavedDraft.ShouldNotBeNull();
        vm.SavedDraft!.SMatrix.ShouldBeNull();      // black box, no invented physics
        vm.SavedDraft.Pins.Count.ShouldBe(2);
        vm.SavedDraft.GdsFactoryFunction.ShouldBe("cspdk.sin300.coupler");
    }

    [Fact]
    public async Task ComputeSMatrix_failure_does_not_produce_a_model()
    {
        var (vm, fdtd) = Build();
        fdtd.Setup(f => f.CheckAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FdtdAvailability(true, ""));
        fdtd.Setup(f => f.SolveAsync(It.IsAny<FdtdSMatrixRequest>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FdtdSMatrixResult.Fail("solver blew up"));

        await vm.RunPreviewCommand.ExecuteAsync(null);
        await vm.ComputeSMatrixCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);

        vm.StatusText.ShouldContain("solver blew up");
        vm.SavedDraft!.SMatrix.ShouldBeNull();       // failed FDTD => still no model, never fake
    }

    [Fact]
    public async Task Save_requires_a_name()
    {
        var (vm, _) = Build(withFdtd: false);
        vm.ComponentName = "   ";
        await vm.RunPreviewCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);
        vm.SavedDraft.ShouldBeNull();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
```

Passe `FdtdAvailability`-Konstruktion an die echte Form an (Step 1). Wenn Moq an den Preview-Service-Typen scheitert, verwende das in Task 4 eingeführte `IComponentPreviewRenderer` und baue den Extractor darüber.

- [ ] **Step 3: Run tests to verify they fail**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" NewComponentViewModel`
Expected: FAIL.

- [ ] **Step 4: Implement `NewComponentViewModel`**

Implementiere das ViewModel gemäß den Design-Notes. Halte es unter 250 Zeilen (lagere das Pin-Mapping `OverridePinData` → `PhysicalPinDraft` in eine private statische Hilfsmethode aus). Kernpunkte:
- `Processes` als `IReadOnlyList<ProcessDefinition>` exponieren (Dropdown-Backing).
- `RunPreview`/`ComputeSMatrix`/`Save` als `async` `[RelayCommand]`; `IsBusy` schützt vor Reentrancy.
- `ComputeSMatrix` NUR wenn `_lastPreview?.Success == true && SelectedProcess != null`.
- FDTD-Fehler: `StatusText` = `result.Error`, `_computedModel = null` — NIE eine Fake-Matrix.
- `Save`: früh raus bei leerem Namen/keinem Preview/keinem Prozess (setzt `StatusText`, lässt `SavedDraft` null).
- Kollision: wenn `store.ComponentExists(process, name)` und `ConfirmOverwrite` gesetzt → nur speichern, wenn bestätigt.

- [ ] **Step 5: Run tests to verify they pass**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" NewComponentViewModel`
Expected: PASS (3/3).

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/ViewModels/Components/AddCustomComponent/NewComponentViewModel.cs UnitTests/Components/AddCustomComponent/NewComponentViewModelTests.cs
git commit -m "(+) NewComponentViewModel: orchestrate name/geometry/process/FDTD/save for a custom component"
```

---

### Task 6: UI-Fenster, DI-Extension und LeftPanel-Integration

**Files:**
- Create: `CAP.Avalonia/Views/NewComponentWindow.axaml` (+ `.axaml.cs`)
- Create: `CAP.Avalonia/DI/AddCustomComponentFeature.cs`
- Modify: `CAP.Avalonia/App.axaml.cs` (Aufruf der Feature-Extension)
- Modify: `CAP.Avalonia/ViewModels/Panels/LeftPanelViewModel.cs` (`[RelayCommand] OpenNewComponent` + Registrierung)
- Modify: `CAP.Avalonia/Views/MainWindow.axaml` („+"-Button an der Komponenten-Library)
- Test: `UnitTests/Components/AddCustomComponent/LeftPanelNewComponentTests.cs`

**Interfaces:**
- Consumes: `NewComponentViewModel` (Task 5, `SavedDraft`, `SavedProcessName`, `Saved`-Event); `PdkTemplateConverter.ConvertToTemplate(PdkComponentDraft, string pdkName, string? nazcaModule, string? gdsFactoryRoutingXs = null) : ComponentTemplate`; `PdkManagerViewModel.RegisterPdk(string, string?, bool, int)`; `UserPreferencesService.AddUserPdkPath(string)`; `LeftPanelViewModel.AllTemplates`/`Categories`/`FilterComponents()`.
- Produces: `LeftPanelViewModel.RegisterSavedCustomComponent(PdkComponentDraft draft, string pdkName, string filePath)` — konvertiert Draft → Template, fügt zu `AllTemplates` + Kategorie hinzu, ruft `RegisterPdk` + `AddUserPdkPath`, `FilterComponents()`. (Öffentlich, damit testbar ohne Fenster.)

- [ ] **Step 1: Read the integration points**

Lies `LeftPanelViewModel.cs` Z324-411 (`LoadPdk`, `LoadPdkFromJsonFileAsync`, `ConvertPdkComponentToTemplate`) — spiegle exakt dieses Registrier-Muster. Lies eine bestehende DI-Extension (`CAP.Avalonia/DI/FdtdFeatureExtensions.cs`) und wie `App.axaml.cs` sie aufruft. Lies `MainWindow.axaml` an der Stelle der Komponenten-Library, um den „+"-Button zu platzieren.

- [ ] **Step 2: Write the failing test (LeftPanel-Registrierung, ohne Fenster)**

```csharp
using System.Linq;
using CAP.Avalonia.ViewModels.Panels;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components.AddCustomComponent;

public class LeftPanelNewComponentTests
{
    [Fact]
    public void RegisterSavedCustomComponent_adds_template_to_the_library()
    {
        var vm = LeftPanelViewModelTestFactory.Create();  // reuse existing test construction helper
        int before = vm.AllTemplates.Count;

        var draft = new PdkComponentDraft
        {
            Name = "My Coupler", Category = "Custom",
            GdsFactoryFunction = "cspdk.sin300.coupler",
            WidthMicrometers = 10, HeightMicrometers = 2
        };
        vm.RegisterSavedCustomComponent(draft, "My CornerStone Components", "C:/tmp/x.json");

        vm.AllTemplates.Count.ShouldBe(before + 1);
        vm.AllTemplates.ShouldContain(t => t.Name == "My Coupler");
    }
}
```

Wenn es keinen Konstruktions-Helper für `LeftPanelViewModel` gibt, sieh in bestehenden `LeftPanelViewModel`-Tests nach, wie das VM dort gebaut wird, und repliziere das inline (KEINEN produktiven Test-only-Helper hinzufügen).

- [ ] **Step 3: Run test to verify it fails**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" LeftPanelNewComponent`
Expected: FAIL (`RegisterSavedCustomComponent` existiert nicht).

- [ ] **Step 4: Implement `RegisterSavedCustomComponent` in `LeftPanelViewModel`**

Spiegle `LoadPdkFromJsonFileAsync`: `var template = ConvertPdkComponentToTemplate(draft, pdkName, null);` → `AllTemplates.Add(template);` → Kategorie hinzufügen falls neu → `PdkManager.RegisterPdk(pdkName, filePath, false, 1)` (oder Anzahl aktualisieren, wenn bereits registriert — nutze `PdkManager.IsPdkLoaded(filePath)` um doppelte Registrierung zu vermeiden) → `_preferencesService.AddUserPdkPath(filePath)` → `FilterComponents()`. XML-Doku.

- [ ] **Step 5: Add `[RelayCommand] OpenNewComponent` in `LeftPanelViewModel`**

Öffnet `NewComponentWindow` mit einem `NewComponentViewModel` (Prozessliste via `GetLoadedPdkProcessEntries()`/`GetLoadedPdkDrafts()` gefiltert auf `Process != null`). Abonniert `Saved` und ruft `RegisterSavedCustomComponent(vm.SavedDraft!, ...)`. Da das Öffnen eines Fensters nicht headless-testbar ist, halte die Command-Methode dünn und delegiere die testbare Logik an `RegisterSavedCustomComponent` (in Step 4 getestet). Die Fenster-Instanziierung erfolgt über einen injizierten Dialog-Öffner oder `Func<NewComponentViewModel, Task>` (analog zu `ConfirmSaveToPdk`-Muster), damit die Command-Methode selbst frei von direkter View-Kopplung bleibt.

- [ ] **Step 6: Create `NewComponentWindow.axaml` (+ code-behind)**

Minimales Avalonia-Fenster mit `x:DataType="vm:NewComponentViewModel"`, compiled bindings: TextBox `ComponentName`; ComboBox Backend (Nazca/GdsFactory); TextBoxes `Module`/`Function`/`Parameters`; ComboBox `Processes`/`SelectedProcess`; Buttons `RunPreviewCommand`, `ComputeSMatrixCommand`, `SaveCommand`; `StatusText`-TextBlock; `IsBusy`-Indikator. Folge dem Layout-Stil von `ProcessManagementWindow.axaml`. Code-behind nur `InitializeComponent()`.

- [ ] **Step 7: Create the DI extension and wire it**

`CAP.Avalonia/DI/AddCustomComponentFeature.cs`:

```csharp
using CAP.Avalonia.Services.AddCustomComponent;
using CAP_DataAccess.Components.AddCustomComponent;
using Microsoft.Extensions.DependencyInjection;

namespace CAP.Avalonia.DI;

/// <summary>DI registrations for the "add custom component → user PDK" feature.</summary>
internal static class AddCustomComponentFeature
{
    /// <summary>Registers the user-PDK store and the geometry extractor.</summary>
    public static IServiceCollection AddAddCustomComponentFeature(this IServiceCollection services)
    {
        services.AddSingleton(_ => UserPdkStore.CreateDefault());
        services.AddSingleton<ComponentGeometryExtractor>();
        return services;
    }
}
```

`ComponentGeometryExtractor` braucht `NazcaComponentPreviewService` + `GdsFactoryComponentPreviewService` aus DI — prüfe, dass beide registriert sind (sie werden fürs Override-Feature registriert); falls nicht als beide Typen verfügbar, registriere sie in dieser Extension analog zur Preview-Registrierung des Override-Features. In `App.axaml.cs` neben `AddFdtdFeature()`: `services.AddAddCustomComponentFeature();`

- [ ] **Step 8: Add the "+" button to `MainWindow.axaml`**

An der Komponenten-Library (dort wo `FilteredTemplates` gelistet werden) ein kompakter Button mit Tooltip „Neue Komponente…" gebunden an `OpenNewComponentCommand` des `LeftPanelViewModel`. Folge dem vorhandenen Button-Stil des linken Panels.

- [ ] **Step 9: Build + run the focused test**

Run: `dotnet build -clp:ErrorsOnly`
Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" LeftPanelNewComponent`
Expected: Build 0 Fehler; Test PASS.

- [ ] **Step 10: Commit**

```bash
git add CAP.Avalonia/Views/NewComponentWindow.axaml CAP.Avalonia/Views/NewComponentWindow.axaml.cs CAP.Avalonia/DI/AddCustomComponentFeature.cs CAP.Avalonia/App.axaml.cs CAP.Avalonia/ViewModels/Panels/LeftPanelViewModel.cs CAP.Avalonia/Views/MainWindow.axaml UnitTests/Components/AddCustomComponent/LeftPanelNewComponentTests.cs
git commit -m "(+) New Component window + '+' library button, DI wiring and LeftPanel registration"
```

---

### Task 7: Architektur-Test für den Slice

**Files:**
- Modify: `UnitTests/Architecture/VerticalSliceConventionTests.cs` (Feature in die enumerierte Liste aufnehmen, falls das Muster das verlangt) ODER Create: `UnitTests/Architecture/AddCustomComponentSliceTests.cs`
- Test: derselbe.

**Interfaces:**
- Consumes: die bestehende Architektur-Test-Infrastruktur.
- Produces: eine Assertion, dass die `AddCustomComponent`-Namespaces nur erlaubte Namespaces importieren, und dass `UserPdkStore.ResolvePath` nie einen Pfad unter `CAP-DataAccess/PDKs` liefert.

- [ ] **Step 1: Read the existing architecture test**

Lies `UnitTests/Architecture/VerticalSliceConventionTests.cs` und `CrossPlatformProcessLaunchTests.cs`, um Stil/Mechanik zu übernehmen.

- [ ] **Step 2: Write the failing test**

```csharp
using System;
using System.IO;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

public class AddCustomComponentSliceTests
{
    [Fact]
    public void UserPdk_path_is_never_inside_the_bundled_pdk_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "lunima-arch-" + Guid.NewGuid().ToString("N"));
        var store = new UserPdkStore(root, new PdkJsonSaver(), new PdkLoader());
        var path = store.ResolvePath(new ProcessDefinition { Name = "CornerStone SiN" });

        path.Replace('\\', '/').ShouldNotContain("/PDKs/");
        path.ShouldStartWith(root);
    }
}
```

- [ ] **Step 3: Run to verify it fails, implement if needed, run to verify it passes**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" AddCustomComponentSlice`
(Der Test sollte nach Task 1 direkt grün sein — wenn ja, ist das die Verifikation; wenn die Vertical-Slice-Konvention eine Enumeration verlangt, ergänze den Slice dort und stelle grün.)

- [ ] **Step 4: Commit**

```bash
git add UnitTests/Architecture/AddCustomComponentSliceTests.cs
git commit -m "(+) Architecture test: user PDK never lands in the bundled PDK folder"
```

---

## Self-Review

**Spec coverage:**
- „+"-Fenster in Library → Task 6. Name/Prozess/Geometrie/FDTD/Save-Flow → Task 5. User-PDK pro Prozess, nie Foundry → Task 1 + Task 7. Ehrliche S-Matrix (FDTD/Blackbox/2-Port) → Task 2 + Task 5. Funktions-Referenz-Render (gdsfactory Rawcode-Wrapper, nazca module-mode) → Task 4. FDTD aus Preview → Task 3. DI/Registrierung → Task 6. Cross-Platform-Pfade/Kultur → Task 1. Alle Spec-Abschnitte abgedeckt.
- Rawcode-Authoring ist explizit v2 (Spec) → keine Tasks, korrekt.

**Placeholder scan:** Keine TBD/„handle edge cases" ohne Code. Alle Code-Steps enthalten echten Code oder eine präzise Read-then-mirror-Anweisung mit exakter Datei/Signatur.

**Type consistency:** `GeometryReference`/`GeometryBackend` (Task 4) konsistent in Task 5 genutzt. `GeometryExtractResult.Raw`/`Pins`/`WidthUm`/`HeightUm` (Task 4) → Task 5 Save/Compute. `BuildFromPreview` (Task 3) → Task 5 Compute. `FdtdSMatrixToDraftConverter.FromFdtd/BlackBox/LosslessTwoPort` (Task 2) → Task 5 Save. `UserPdkStore.Save/ComponentExists/ResolvePath` (Task 1) → Task 5/6/7. `RegisterSavedCustomComponent` (Task 6) konsistent mit `NewComponentViewModel.SavedDraft` (Task 5).

**Bekannte Verifikationspunkte (in den Steps als „Read the real signatures" markiert):** exakte Feldnamen von `WavelengthSMatrixEntry`, `FdtdAvailability`, `PhysicalPinDraft`, `OverridePinData`, `PdkLoader`-ctor, und Moq-Fähigkeit der Preview-Services (Fallback: `IComponentPreviewRenderer`). Diese sind bewusst dem Implementierer überlassen, weil sie 1 Read entfernt sind und exakt benannt wurden.
