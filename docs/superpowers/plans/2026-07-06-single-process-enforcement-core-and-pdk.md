# Core process policy and PDK metadata

> Historical July 2026 implementation plan. [Plan index](2026-07-06-single-process-enforcement.md).
> For current design rules, see [Architecture](../../../ARCHITECTURE.md) and [Codebase map](../../CODEBASE_MAP.md).

# Single-Process Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock every design to exactly one fabrication process (physically compatible PDKs), chosen consciously at New-Design time, with a persistent indicator, a process-filtered component library, and placement/paste enforcement.

**Architecture:** Pure process logic (fingerprint, compatibility, grouping, placement policy) lives in `CAP-Core` operating on primitives; extraction from the `PdkDraft` DTO lives in `CAP-DataAccess` (which references Core). ViewModels hold the active-process selection, persist it in the `.lun`, drive the library filter, and enforce at the two placement paths. Supersedes PR #602 (PDK-name lock).

**Tech Stack:** C# / .NET 10, Avalonia 11 / CommunityToolkit.Mvvm, xUnit + Shouldly + Moq. Spec: `docs/superpowers/specs/2026-07-06-single-process-enforcement-design.md`.

## Global Constraints

- Tests run via `py "$env:USERPROFILE\.cap-tools\smart_test.py" <Pattern>` with `$env:PYTHONUTF8='1'` (NOT `dotnet test` directly). Build: `dotnet build ConnectAPICPro.sln`.
- Max 250 lines per NEW file; hard cap 500 lines/file (enforced by `FileSizeLimitTests`).
- `CAP-Core` must NOT reference `CAP-DataAccess`. Pure logic → Core; DTO-touching code → DataAccess.
- Every public class/method gets XML docs. `_camelCase` private fields, PascalCase public.
- No magic numbers — tolerances are named constants.
- Machine-facing/number formatting uses `CultureInfo.InvariantCulture`.
- Built-in/tool exemption: a component whose PDK source is `null`, `""`, or `"Built-in"` is always allowed.
- Compatibility tolerances: **core thickness ±5 nm**, **design wavelength ±40 nm**; core material + cladding are exact (case-insensitive).

---

## Phase 1 — Core process logic (pure, no UI)

### Task 1: `ProcessFingerprint` + `ProcessCompatibility`

**Files:**
- Create: `Connect-A-Pic-Core/Components/Process/ProcessFingerprint.cs`
- Create: `Connect-A-Pic-Core/Components/Process/ProcessCompatibility.cs`
- Test: `UnitTests/Components/Process/ProcessCompatibilityTests.cs`

**Interfaces:**
- Produces: `record ProcessFingerprint(string? CoreMaterial, double? CoreThicknessNm, string? Cladding, int DesignWavelengthNm, string? ProcessName)` with `bool IsSpecified`.
- Produces: `static bool ProcessCompatibility.AreCompatible(ProcessFingerprint a, ProcessFingerprint b)`, `const double CoreThicknessToleranceNm = 5`, `const int WavelengthToleranceNm = 40`.

- [ ] **Step 1: Write the failing test**

```csharp
using CAP_Core.Components.Process;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Process;

public class ProcessCompatibilityTests
{
    private static ProcessFingerprint Fp(string? mat, double? thick, string? clad, int wl) =>
        new(mat, thick, clad, wl, ProcessName: null);

    [Fact]
    public void SameMaterialWithinTolerances_IsCompatible()
    {
        ProcessCompatibility.AreCompatible(
            Fp("Si", 220, "SiO2", 1550), Fp("si", 222, "sio2", 1560)).ShouldBeTrue();
    }

    [Fact]
    public void DifferentCoreMaterial_IsNotCompatible()
    {
        ProcessCompatibility.AreCompatible(
            Fp("Si", 220, "SiO2", 1550), Fp("SiN", 220, "SiO2", 1550)).ShouldBeFalse();
    }

    [Fact]
    public void ThicknessBeyondTolerance_IsNotCompatible()
    {
        ProcessCompatibility.AreCompatible(
            Fp("Si", 220, "SiO2", 1550), Fp("Si", 340, "SiO2", 1550)).ShouldBeFalse();
    }

    [Fact]
    public void WavelengthBeyondTolerance_IsNotCompatible()
    {
        ProcessCompatibility.AreCompatible(
            Fp("Si", 220, "SiO2", 1550), Fp("Si", 220, "SiO2", 1310)).ShouldBeFalse();
    }

    [Fact]
    public void UnspecifiedFingerprint_IsNeverCompatible()
    {
        var unspecified = Fp(null, null, null, 1550);
        ProcessCompatibility.AreCompatible(unspecified, unspecified).ShouldBeFalse();
        unspecified.IsSpecified.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ProcessCompatibility`
Expected: FAIL — types `ProcessFingerprint` / `ProcessCompatibility` do not exist.

- [ ] **Step 3: Implement `ProcessFingerprint`**

```csharp
namespace CAP_Core.Components.Process;

/// <summary>
/// The physical identity of a fabrication process, derived from a PDK (issue #570).
/// Two PDKs are usable on the same chip when their fingerprints are compatible.
/// </summary>
/// <param name="CoreMaterial">Waveguide core material name (e.g. "Si", "SiN"); null if unspecified.</param>
/// <param name="CoreThicknessNm">Core layer thickness in nm; null if unspecified.</param>
/// <param name="Cladding">Cladding material name (e.g. "SiO2"); null if unspecified.</param>
/// <param name="DesignWavelengthNm">Representative design wavelength in nm.</param>
/// <param name="ProcessName">Human-readable process label for display; not used for matching.</param>
public sealed record ProcessFingerprint(
    string? CoreMaterial,
    double? CoreThicknessNm,
    string? Cladding,
    int DesignWavelengthNm,
    string? ProcessName)
{
    /// <summary>True when the fingerprint carries enough physical data to group by (core material present).</summary>
    public bool IsSpecified => !string.IsNullOrWhiteSpace(CoreMaterial);
}
```

- [ ] **Step 4: Implement `ProcessCompatibility`**

```csharp
using System;

namespace CAP_Core.Components.Process;

/// <summary>
/// Decides whether two <see cref="ProcessFingerprint"/>s describe the same fabrication
/// process (issue #570). Core material and cladding must match exactly (case-insensitive);
/// core thickness and design wavelength must fall within a small tolerance.
/// </summary>
public static class ProcessCompatibility
{
    /// <summary>Max core-thickness difference (nm) still considered the same process.</summary>
    public const double CoreThicknessToleranceNm = 5;

    /// <summary>Max design-wavelength difference (nm) still considered the same process.</summary>
    public const int WavelengthToleranceNm = 40;

    /// <summary>Returns true when both fingerprints are specified and physically compatible.</summary>
    public static bool AreCompatible(ProcessFingerprint a, ProcessFingerprint b)
    {
        if (!a.IsSpecified || !b.IsSpecified)
            return false;

        if (!string.Equals(a.CoreMaterial, b.CoreMaterial, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(a.Cladding, b.Cladding, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Math.Abs((a.CoreThicknessNm ?? 0) - (b.CoreThicknessNm ?? 0)) > CoreThicknessToleranceNm)
            return false;

        return Math.Abs(a.DesignWavelengthNm - b.DesignWavelengthNm) <= WavelengthToleranceNm;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ProcessCompatibility`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add Connect-A-Pic-Core/Components/Process/ProcessFingerprint.cs Connect-A-Pic-Core/Components/Process/ProcessCompatibility.cs UnitTests/Components/Process/ProcessCompatibilityTests.cs
git commit -m "(+) Single-process: ProcessFingerprint + compatibility rule (#570)"
```

---

### Task 2: `ProcessCatalog` — group compatible PDKs

**Files:**
- Create: `Connect-A-Pic-Core/Components/Process/ProcessGroup.cs`
- Create: `Connect-A-Pic-Core/Components/Process/ProcessCatalog.cs`
- Test: `UnitTests/Components/Process/ProcessCatalogTests.cs`

**Interfaces:**
- Consumes: `ProcessFingerprint`, `ProcessCompatibility.AreCompatible`.
- Produces: `record PdkProcessEntry(string PdkName, ProcessFingerprint Fingerprint)`.
- Produces: `record ProcessGroup(string DisplayName, ProcessFingerprint Fingerprint, IReadOnlyList<string> MemberPdkNames)`.
- Produces: `static IReadOnlyList<ProcessGroup> ProcessCatalog.BuildGroups(IEnumerable<PdkProcessEntry> pdks)`.

- [ ] **Step 1: Write the failing test**

```csharp
using CAP_Core.Components.Process;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Process;

public class ProcessCatalogTests
{
    private static PdkProcessEntry Pdk(string name, string? mat, double? thick, string? clad, int wl, string? proc = null) =>
        new(name, new ProcessFingerprint(mat, thick, clad, wl, proc));

    [Fact]
    public void CompatiblePdks_CollapseIntoOneGroup()
    {
        var groups = ProcessCatalog.BuildGroups(new[]
        {
            Pdk("Foundry SOI", "Si", 220, "SiO2", 1550, "SOI 220"),
            Pdk("Custom SOI",  "Si", 221, "SiO2", 1555),
        });

        groups.Count.ShouldBe(1);
        groups[0].MemberPdkNames.ShouldBe(new[] { "Foundry SOI", "Custom SOI" }, ignoreOrder: true);
    }

    [Fact]
    public void IncompatiblePdks_FormSeparateGroups()
    {
        var groups = ProcessCatalog.BuildGroups(new[]
        {
            Pdk("SOI",  "Si",  220, "SiO2", 1550),
            Pdk("SiNx", "SiN", 340, "SiO2", 1550),
        });
        groups.Count.ShouldBe(2);
    }

    [Fact]
    public void UnspecifiedPdk_IsItsOwnSingletonGroup()
    {
        var groups = ProcessCatalog.BuildGroups(new[]
        {
            Pdk("Legacy A", null, null, null, 1550),
            Pdk("Legacy B", null, null, null, 1550),
        });
        groups.Count.ShouldBe(2);   // never merge unspecified fingerprints
    }

    [Fact]
    public void GroupDisplayName_PrefersSharedProcessName()
    {
        var groups = ProcessCatalog.BuildGroups(new[] { Pdk("P", "Si", 220, "SiO2", 1550, "AMF SOI 220nm") });
        groups[0].DisplayName.ShouldBe("AMF SOI 220nm");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ProcessCatalog`
Expected: FAIL — `ProcessCatalog` / `ProcessGroup` / `PdkProcessEntry` missing.

- [ ] **Step 3: Implement `ProcessGroup` + `PdkProcessEntry`**

```csharp
using System.Collections.Generic;

namespace CAP_Core.Components.Process;

/// <summary>One PDK paired with the process fingerprint extracted from it (issue #570).</summary>
public sealed record PdkProcessEntry(string PdkName, ProcessFingerprint Fingerprint);

/// <summary>
/// A selectable fabrication process: the set of loaded PDKs whose fingerprints are mutually
/// compatible, so their components may share one chip (issue #570).
/// </summary>
public sealed record ProcessGroup(
    string DisplayName,
    ProcessFingerprint Fingerprint,
    IReadOnlyList<string> MemberPdkNames);
```

- [ ] **Step 4: Implement `ProcessCatalog`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace CAP_Core.Components.Process;

/// <summary>
/// Groups the currently loaded PDKs into processes by compatibility (issue #570). PDKs with
/// compatible fingerprints collapse into one group; unspecified PDKs each stay a singleton.
/// No persistence — the catalog is derived fresh from whatever PDKs are loaded.
/// </summary>
public static class ProcessCatalog
{
    /// <summary>Builds the process groups from the given PDK fingerprints.</summary>
    public static IReadOnlyList<ProcessGroup> BuildGroups(IEnumerable<PdkProcessEntry> pdks)
    {
        var groups = new List<List<PdkProcessEntry>>();

        foreach (var entry in pdks)
        {
            var target = entry.Fingerprint.IsSpecified
                ? groups.FirstOrDefault(g =>
                    g[0].Fingerprint.IsSpecified &&
                    ProcessCompatibility.AreCompatible(g[0].Fingerprint, entry.Fingerprint))
                : null;   // unspecified never joins an existing group

            if (target == null)
                groups.Add(new List<PdkProcessEntry> { entry });
            else
                target.Add(entry);
        }

        return groups.Select(ToGroup).ToList();
    }

    private static ProcessGroup ToGroup(List<PdkProcessEntry> members)
    {
        var fp = members[0].Fingerprint;
        return new ProcessGroup(DeriveDisplayName(members), fp, members.Select(m => m.PdkName).ToList());
    }

    private static string DeriveDisplayName(List<PdkProcessEntry> members)
    {
        var fp = members[0].Fingerprint;
        if (!string.IsNullOrWhiteSpace(fp.ProcessName))
            return fp.ProcessName!;
        if (fp.IsSpecified)
            return $"{fp.CoreMaterial} {fp.CoreThicknessNm:0} nm · {fp.Cladding} · {fp.DesignWavelengthNm} nm";
        return members[0].PdkName;   // unspecified singleton
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ProcessCatalog`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add Connect-A-Pic-Core/Components/Process/ProcessGroup.cs Connect-A-Pic-Core/Components/Process/ProcessCatalog.cs UnitTests/Components/Process/ProcessCatalogTests.cs
git commit -m "(+) Single-process: ProcessCatalog groups compatible PDKs (#570)"
```

---

### Task 3: `ActiveProcessSelection` + `SingleProcessPolicy`

**Files:**
- Create: `Connect-A-Pic-Core/Components/Process/ActiveProcessSelection.cs`
- Create: `Connect-A-Pic-Core/Components/Process/SingleProcessPolicy.cs`
- Test: `UnitTests/Components/Process/SingleProcessPolicyTests.cs`

**Interfaces:**
- Consumes: `ProcessGroup`, `ProcessFingerprint`.
- Produces: `record ActiveProcessSelection` with `string DisplayName`, `ProcessFingerprint? Fingerprint`, `IReadOnlyList<string> MemberPdkNames`, `bool IsPlayground`; statics `Playground()` and `ForGroup(ProcessGroup)`.
- Produces: `static (bool IsAllowed, string? BlockReason) SingleProcessPolicy.CheckPlacement(ActiveProcessSelection? active, string? componentPdkName)`.
- Produces: `static bool SingleProcessPolicy.IsBuiltIn(string? pdkSource)`.

- [ ] **Step 1: Write the failing test**

```csharp
using CAP_Core.Components.Process;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Process;

public class SingleProcessPolicyTests
{
    private static ActiveProcessSelection Soi() => ActiveProcessSelection.ForGroup(
        new ProcessGroup("SOI 220", new ProcessFingerprint("Si", 220, "SiO2", 1550, "SOI 220"),
            new[] { "Foundry SOI", "Custom SOI" }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Built-in")]
    public void BuiltInComponent_IsAlwaysAllowed(string? pdk)
    {
        SingleProcessPolicy.CheckPlacement(Soi(), pdk).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void MemberPdk_IsAllowed()
    {
        SingleProcessPolicy.CheckPlacement(Soi(), "Custom SOI").IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void ForeignPdk_IsBlockedWithReason()
    {
        var (ok, reason) = SingleProcessPolicy.CheckPlacement(Soi(), "HHI-InP");
        ok.ShouldBeFalse();
        reason.ShouldNotBeNull();
        reason!.ShouldContain("HHI-InP");
        reason.ShouldContain("SOI 220");
    }

    [Fact]
    public void Playground_AllowsAnything()
    {
        SingleProcessPolicy.CheckPlacement(ActiveProcessSelection.Playground(), "HHI-InP").IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public void NoActiveProcess_AllowsAnything()
    {
        SingleProcessPolicy.CheckPlacement(null, "HHI-InP").IsAllowed.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SingleProcessPolicy`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement `ActiveProcessSelection`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace CAP_Core.Components.Process;

/// <summary>
/// The process a design is currently locked to (issue #570): a real process (with its member
/// PDKs), or Playground (mixing allowed, not manufacturable). Persisted with the design.
/// </summary>
public sealed record ActiveProcessSelection(
    string DisplayName,
    ProcessFingerprint? Fingerprint,
    IReadOnlyList<string> MemberPdkNames,
    bool IsPlayground)
{
    /// <summary>The sandbox selection: any component allowed, chip not manufacturable.</summary>
    public static ActiveProcessSelection Playground() =>
        new("Playground", Fingerprint: null, MemberPdkNames: new List<string>(), IsPlayground: true);

    /// <summary>Locks to a derived process group.</summary>
    public static ActiveProcessSelection ForGroup(ProcessGroup group) =>
        new(group.DisplayName, group.Fingerprint, group.MemberPdkNames.ToList(), IsPlayground: false);
}
```

- [ ] **Step 4: Implement `SingleProcessPolicy`**

```csharp
using System;
using System.Linq;

namespace CAP_Core.Components.Process;

/// <summary>
/// Enforces the single-process-per-design rule at component placement (issue #570).
/// Process-keyed successor to the PDK-name-based policy from PR #602.
/// </summary>
public static class SingleProcessPolicy
{
    /// <summary>The reserved PDK-source label for process-agnostic built-in/tool components.</summary>
    public const string BuiltInSource = "Built-in";

    /// <summary>True when the PDK source denotes a built-in / tool (process-agnostic) component.</summary>
    public static bool IsBuiltIn(string? pdkSource) =>
        string.IsNullOrWhiteSpace(pdkSource) ||
        string.Equals(pdkSource, BuiltInSource, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Decides whether a component from <paramref name="componentPdkName"/> may be placed on a
    /// design locked to <paramref name="active"/>. Built-ins, Playground, and an unset selection
    /// always pass; otherwise the component's PDK must be a member of the active process.
    /// </summary>
    public static (bool IsAllowed, string? BlockReason) CheckPlacement(
        ActiveProcessSelection? active, string? componentPdkName)
    {
        if (IsBuiltIn(componentPdkName))
            return (true, null);

        if (active == null || active.IsPlayground)
            return (true, null);

        if (active.MemberPdkNames.Contains(componentPdkName!, StringComparer.OrdinalIgnoreCase))
            return (true, null);

        return (false,
            $"This component belongs to '{componentPdkName}', but the chip is locked to the process " +
            $"'{active.DisplayName}'. A monolithic design uses one process — start a new design (or use " +
            "Playground) to mix processes.");
    }
}
```

- [ ] **Step 5: Run to verify pass** — `... SingleProcessPolicy` → PASS (7 cases).

- [ ] **Step 6: Commit**

```bash
git add Connect-A-Pic-Core/Components/Process/ActiveProcessSelection.cs Connect-A-Pic-Core/Components/Process/SingleProcessPolicy.cs UnitTests/Components/Process/SingleProcessPolicyTests.cs
git commit -m "(+) Single-process: ActiveProcessSelection + placement policy (#570)"
```

---

## Phase 2 — PDK metadata extraction (DataAccess)

### Task 4: `ProcessDefinition.CoreThicknessNm` + `ProcessFingerprintFactory`

**Files:**
- Modify: `CAP-DataAccess/Components/ComponentDraftMapper/DTOs/ProcessDefinition.cs` (add `CoreThicknessNm`)
- Create: `CAP-DataAccess/Components/ComponentDraftMapper/ProcessFingerprintFactory.cs`
- Test: `UnitTests/Components/Process/ProcessFingerprintFactoryTests.cs`

**Interfaces:**
- Consumes: `PdkDraft` (has `Process`, `DefaultWavelengthNm`), `ProcessDefinition` (has `Materials` with `Role`, and the new `CoreThicknessNm`), `ProcessFingerprint`.
- Produces: `static ProcessFingerprint ProcessFingerprintFactory.From(PdkDraft draft)`.

- [ ] **Step 1: Write the failing test**

```csharp
using CAP_Core.Components.Process;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Process;

public class ProcessFingerprintFactoryTests
{
    [Fact]
    public void From_PdkWithProcess_ExtractsMaterialsThicknessAndWavelength()
    {
        var draft = new PdkDraft
        {
            Name = "Demo", DefaultWavelengthNm = 1550,
            Process = new ProcessDefinition
            {
                Name = "SOI 220", CoreThicknessNm = 220,
                Materials =
                {
                    new ProcessMaterial { Name = "Si",   Role = "core" },
                    new ProcessMaterial { Name = "SiO2", Role = "cladding" },
                },
            },
        };

        var fp = ProcessFingerprintFactory.From(draft);

        fp.CoreMaterial.ShouldBe("Si");
        fp.Cladding.ShouldBe("SiO2");
        fp.CoreThicknessNm.ShouldBe(220);
        fp.DesignWavelengthNm.ShouldBe(1550);
        fp.ProcessName.ShouldBe("SOI 220");
        fp.IsSpecified.ShouldBeTrue();
    }

    [Fact]
    public void From_PdkWithoutProcess_IsUnspecified()
    {
        var fp = ProcessFingerprintFactory.From(new PdkDraft { Name = "Legacy", DefaultWavelengthNm = 1550 });
        fp.IsSpecified.ShouldBeFalse();
        fp.DesignWavelengthNm.ShouldBe(1550);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `... ProcessFingerprintFactory` → FAIL (factory + `CoreThicknessNm` missing).

- [ ] **Step 3: Add `CoreThicknessNm` to `ProcessDefinition`**

In `CAP-DataAccess/Components/ComponentDraftMapper/DTOs/ProcessDefinition.cs`, add inside `ProcessDefinition` after `Version`:

```csharp
        /// <summary>
        /// Defining waveguide-core thickness in nm (e.g. 220 for 220 nm SOI). The key
        /// physical axis for process compatibility (issue #570); optional so old PDKs parse.
        /// </summary>
        [JsonPropertyName("coreThicknessNm")]
        public double? CoreThicknessNm { get; set; }
```

- [ ] **Step 4: Implement `ProcessFingerprintFactory`**

```csharp
using System;
using System.Linq;
using CAP_Core.Components.Process;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>
/// Builds a <see cref="ProcessFingerprint"/> from a loaded <see cref="PdkDraft"/> (issue #570).
/// Core/cladding come from the process materials' <c>Role</c>; wavelength from the PDK default;
/// thickness from the process. A PDK without a process block yields an unspecified fingerprint.
/// </summary>
public static class ProcessFingerprintFactory
{
    /// <summary>Extracts the process fingerprint for the given PDK.</summary>
    public static ProcessFingerprint From(PdkDraft draft)
    {
        var process = draft.Process;
        var core = MaterialByRole(process, "core");
        var cladding = MaterialByRole(process, "cladding");

        return new ProcessFingerprint(
            CoreMaterial: core,
            CoreThicknessNm: process?.CoreThicknessNm,
            Cladding: cladding,
            DesignWavelengthNm: draft.DefaultWavelengthNm,
            ProcessName: string.IsNullOrWhiteSpace(process?.Name) ? null : process!.Name);
    }

    private static string? MaterialByRole(ProcessDefinition? process, string role) =>
        process?.Materials
            .FirstOrDefault(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase))
            ?.Name;
}
```

- [ ] **Step 5: Run to verify pass** — `... ProcessFingerprintFactory` → PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add CAP-DataAccess/Components/ComponentDraftMapper/DTOs/ProcessDefinition.cs CAP-DataAccess/Components/ComponentDraftMapper/ProcessFingerprintFactory.cs UnitTests/Components/Process/ProcessFingerprintFactoryTests.cs
git commit -m "(+) Single-process: coreThicknessNm + ProcessFingerprintFactory (#570)"
```

---

### Task 5: Populate bundled PDK process blocks

**Files:**
- Modify: `CAP-DataAccess/PDKs/demo-pdk.json` (add top-level `process`)
- Modify: `CAP-DataAccess/PDKs/siepic-ebeam-pdk.json` (add top-level `process`)
- Test: `UnitTests/Components/Process/BundledPdkProcessTests.cs`

**Interfaces:**
- Consumes: `PdkLoader` (loads a PDK file → `PdkDraft`), `ProcessFingerprintFactory`, `ProcessCatalog`.

- [ ] **Step 1: Add the `process` block to `demo-pdk.json`** — insert after the `"defaultWavelengthNm": 1550,` line:

```json
  "process": {
    "name": "Demo SOI 220nm",
    "foundry": "Demo Foundry",
    "coreThicknessNm": 220,
    "materials": [
      { "name": "Si",   "role": "core" },
      { "name": "SiO2", "role": "cladding" }
    ]
  },
```

- [ ] **Step 2: Add the `process` block to `siepic-ebeam-pdk.json`** — insert after its `"defaultWavelengthNm"` line (SiEPIC EBeam is 220 nm SOI at 1550 nm — same process family as Demo on purpose, to exercise grouping):

```json
  "process": {
    "name": "SiEPIC EBeam SOI 220nm",
    "foundry": "Applied Nanotools",
    "coreThicknessNm": 220,
    "materials": [
      { "name": "Si",   "role": "core" },
      { "name": "SiO2", "role": "cladding" }
    ]
  },
```

- [ ] **Step 3: Write the test**

```csharp
using System.IO;
using System.Linq;
using CAP_Core.Components.Process;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Shouldly;
using Xunit;

namespace UnitTests.Components.Process;

public class BundledPdkProcessTests
{
    private static string PdkDir =>
        Path.Combine(TestPaths.RepoRoot, "CAP-DataAccess", "PDKs");   // see note in Step 4

    [Theory]
    [InlineData("demo-pdk.json")]
    [InlineData("siepic-ebeam-pdk.json")]
    public void BundledPdk_HasSpecifiedProcessFingerprint(string file)
    {
        var draft = new PdkLoader().LoadFromFile(Path.Combine(PdkDir, file));
        ProcessFingerprintFactory.From(draft).IsSpecified.ShouldBeTrue();
    }

    [Fact]
    public void DemoAndSiepic_ShareOneProcessGroup()
    {
        var loader = new PdkLoader();
        var entries = new[] { "demo-pdk.json", "siepic-ebeam-pdk.json" }
            .Select(f => loader.LoadFromFile(Path.Combine(PdkDir, f)))
            .Select(d => new PdkProcessEntry(d.Name, ProcessFingerprintFactory.From(d)));

        ProcessCatalog.BuildGroups(entries).Count.ShouldBe(1);
    }
}
```

- [ ] **Step 4: Verify `PdkLoader.LoadFromFile` + `TestPaths.RepoRoot`**

Run: `codegraph_search PdkLoader` — confirm the public load method name; if it differs (e.g. `Load(string)`), use that. For the repo-root path helper, reuse the existing pattern from another test that reads `CAP-DataAccess/PDKs` (search: `grep -rn "CAP-DataAccess.*PDKs" UnitTests`); if no helper exists, resolve via `AppContext.BaseDirectory` walking up to the `.sln` (same approach as `UiScreenshotTests.ResolveOutputDirectory`). Replace `TestPaths.RepoRoot` accordingly — do NOT introduce a new helper if one exists.

- [ ] **Step 5: Run to verify pass** — `... BundledPdkProcess` → PASS (3 cases).

- [ ] **Step 6: Commit**

```bash
git add CAP-DataAccess/PDKs/demo-pdk.json CAP-DataAccess/PDKs/siepic-ebeam-pdk.json UnitTests/Components/Process/BundledPdkProcessTests.cs
git commit -m "(+) Single-process: declare process fingerprints on bundled PDKs (#570)"
```

---

