# Spike #1647 — pilot journeys on the installed newest release

**Question.** Can the newest published release complete the three pilot journeys on
Windows, Linux and macOS from the installed binary alone, through its headless agent CLI,
with no repo checkout on the path?

**Answer: no.** Journeys 1 and 2 pass everywhere with nothing extra installed (journey 2
with spurious pins). Journey 3 (export GDS + pre-flight) needs Python, Nazca, three pip
packages and KLayout everywhere. With those it passes on Windows only:

- On **Linux** the release cannot launch any external process (#1653).
- On **macOS** the Nazca check times out on a cold profile, failing 3 of 4 runs (#1654).
- The **openEBL** half has no CLI entry point on any OS (#1651).

## Method

- `.github/workflows/PilotJourneysSpike.yaml` downloads release **v0.23.0** (newest,
  2026-11-09) and installs it as a user would: MSI (silent, `C:\Program Files\Lunima`),
  portable zip, tar.gz, dmg → `/Applications`. Runners: `windows-latest` (win25),
  `ubuntu-latest` (Ubuntu 26), `macos-latest` (macOS 26, arm64).
- `pilot_journeys.py` starts the installed binary from a sandbox with a fresh
  HOME/APPDATA, so neither a checkout nor the runner profile can stand in for a missing
  file. The checkout is sparse: the two spike scripts, the smoke helper and one GDS.
- Every journey runs in three phases:
  1. **bare**: nothing but the release.
  2. **nazca**: plus a venv with Nazca from nazca-design.org, the URL the app's own
     installer uses.
  3. **full**: plus the openEBL packages (first the exact pip command the app shows) and
     KLayout, installed the way each OS offers it.
- Each step records the app's own message, so you can see whether the app named what was
  missing.
- Journey inputs:
  - J1: the shipped Mach-Zehnder example.
  - J2: `Tools/gds-test-data/gdsfactory_mzi_like.gds`, real gdsfactory output with port
    labels on 1/10.
  - J3: the shipped "EBeam Add-Drop Ring", the Cornerstone deck shipped in
    `scripts/drc`, and both shipped openEBL scripts.
- Runs: [1](https://github.com/aignermax/Lunima/actions/runs/43844901858),
  [2](https://github.com/aignermax/Lunima/actions/runs/43846795813),
  [3](https://github.com/aignermax/Lunima/actions/runs/43847949372),
  [4, final](https://github.com/aignermax/Lunima/actions/runs/43849824501). The job
  summary of each run holds the machine-made table and every app message.

## Result (v0.23.0)

| Journey | Windows (MSI; portable zip identical) | Linux (tar.gz) | macOS arm64 (dmg) |
|---|---|---|---|
| **1** open MZI, simulate, read port powers | ✅ PASS, nothing extra. Powers 0.1389 / 0.1389 | ✅ PASS, nothing extra, same powers | ✅ PASS, nothing extra, same powers |
| **2** import GDS with port markers, get pins | ✅ PASS, nothing extra. 4 cells, 3 abutments, all 9 labelled pins, **plus 8 spurious `heur_*` pins** (#1650) | ✅ same (#1650) | ✅ same (#1650) |
| **3** export GDS + Cornerstone pre-flight | ✅ PASS with extras: Python venv + Nazca, `klayout siepic_ebeam_pdk SiEPIC` (else a stub GDS, #1652), KLayout (choco) + `KLAYOUT` env var | ❌ FAIL in every phase: no child process can start (#1653). The app says "no Python with Nazca found", then "Unable to load shared library 'System.Native'" | ⚠️ FAIL in 3 of 4 runs: the Nazca probe times out (#1654). When it runs: PASS with the same extras + KLayout from the klayout.org dmg (Homebrew cask disabled) + `KLAYOUT` |
| **3** openEBL pre-flight | ⛔ cannot run through the CLI: no tool (#1651). By hand: ✅ 0 errors with the PyPI names (#1649) | ⛔ same (#1651). By hand on a hand-made GDS: ✅ 0 errors | ⛔ same (#1651). By hand: ✅ 0 errors |

### Extra installs per cell, and whether the app named them

| What | Needed for | Named by the app? |
|---|---|---|
| Python + Nazca (venv) | J3 export, all OSes | **Partly.** "Install Nazca in Lunima's Python environment (Settings → Python)" points to the GUI. The CLI message gives no source; Nazca is not on PyPI. On Linux/macOS the message also appears *with* Nazca installed (#1653, #1654) |
| `klayout siepic_ebeam_pdk SiEPIC` | J3 export with real SiEPIC cells; openEBL scripts | **No** for the export: it reports success with stub grating couplers (8,778 B vs 138,434 B; #1652). **Wrong** for openEBL: the app's `pip install … SiEPIC-Tools` fails on every OS, nothing installs (#1649) |
| KLayout desktop/CLI | J3 Cornerstone, all OSes | **Yes**: "KLayout was not found. Install KLayout (klayout.de) or set KLAYOUT to its executable." |
| `KLAYOUT=<path>` | Windows (choco → `C:\Program Files (x86)\KLayout\klayout_app.exe`), macOS (`/Applications/klayout.app/…`); neither is on PATH | **Yes** (same message). On Linux `apt install klayout` is on PATH |

### Bugs filed (all link #1647)

| # | Cell | Summary |
|---|---|---|
| #1653 | J3 × Linux | Single-file Linux build cannot P/Invoke `System.Native`, so every Python/KLayout launch fails. Since v0.21.0 |
| #1654 | J3 × macOS | The 20 s `import nazca` probe kills matplotlib's first font-cache build (cold 27–31 s, warm 2.5–3.7 s) |
| #1652 | J3 × all | `export_gds` drops the script's `[Lunima] WARN … stub boxes kept` and reports success |
| #1649 | J3 openEBL × all | The install hint names `SiEPIC-Tools`, which is not on PyPI (404); the package is `SiEPIC` |
| #1650 | J2 × all | Spurious `heur_*` pins on fully labelled cells (regression against #879) |
| #1651 | J3 openEBL × all | Enhancement: no agent CLI / MCP tool for the openEBL pre-flight (PO triage) |

## Caveats (what a green cell does *not* prove)

- **Hosted runners are not fresh machines.** Every runner already had Python
  (`baseline.txt` in each job). A fresh Windows PC has none, and the app does not ship
  one. The CLI cannot provision the managed Python environment either; that is the GUI's
  Settings → Python, and on Linux it would hit #1653 too. So "Python" is an extra install
  on Windows and macOS that this matrix under-reports.
- **CLI, not GUI.** The GUI entry path (e.g. #1155, Home example tiles) was not measured.
- **macOS signature.** `spctl` rejects the installed app ("code has no resources but
  signature indicates they must be present"; ad-hoc signed, no Team ID). The CLI still
  started from a dmg quarantined like a browser download (exit 0). A Finder launch was
  not measured.
- `nazca-design.org` failed once in 16 installs (run 1, Windows MSI); the repo's vendor
  mirror covered it.
- The Cornerstone SiN deck on an EBeam (SOI) design is only a "does the pre-flight run"
  check. Its single marker ("Correct design area") is expected, not a design verdict.

## Recommendation

The smoke from #1635 would have caught the worst finding (#1653) with one more check:
make the packaged binary start a child process, e.g. `check_foundry_drc` with no KLayout
installed must say "KLayout was not found", not "Unable to load shared library". That is
the critic's "two-check extension of the smoke script", plus this one. #1653 is proposed
in that form. Before the first pilot, a manual GUI run on a clean machine without Python
is still needed: neither this spike nor the smoke covers the GUI path or the missing
Python.
