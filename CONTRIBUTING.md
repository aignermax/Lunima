# Contributing to Lunima

Changes go through reviewed pull requests. `dev` is the integration branch;
`main` receives reviewed releases.

## Workflow

1. Branch off `dev` (`feat/...`, `fix/...`, `docs/...`) and target `dev` with the PR.
2. Keep the PR **small and focused — one concern per PR.** Big, mixed PRs are the
   main thing that makes review painful; split them.
3. Open the PR. To merge you need the **`🔍 xUnit Tests`** check green and
   **1 approval**.
4. After review and passing checks, merge and delete the feature branch.

Releases promote `dev` to `main`; follow [docs/RELEASE.md](docs/RELEASE.md).
Product plans live in [docs/ROADMAP.md](docs/ROADMAP.md).

## Build & test

Install the .NET SDK selected by `global.json` and Python 3. The project targets
.NET 10. Run from the repository root:

```powershell
# Windows
dotnet build
py scripts/run_tests.py
py scripts/install_hooks.py
```

```bash
# Linux/macOS
dotnet build
python3 scripts/run_tests.py
python3 scripts/install_hooks.py
```

The checked-in test helper excludes Slow and Heavy tests by default, sets
`DOTNET_PROCESSOR_COUNT=2`, and saves detailed output to an ignored local log.
A positional substring selects test names while keeping those exclusions;
`--filter` supplies a VSTest expression while retaining exclusions. Only a
specifically selected run with `--include-expensive` removes them. Run local .NET checks
serially. See [docs/TESTING.md](docs/TESTING.md) for examples and CI coverage.

## Source-size checks

Prefer 250 lines or fewer for new files. Authored source and documentation have a hard ceiling of
999 physical lines per file, including tests, AXAML, scripts, CI configuration, Markdown, reStructuredText and text notes.
Separate responsibilities into focused collaborators; do not add partial-class
fragments, minify/encode source or delete assertions to bypass the ceiling.
Data, fixture and generated-artifact scope remains pending clarification.

`scripts/install_hooks.py` installs the local pre-commit guard for a fresh clone.
Check the whole tree or only staged source explicitly:

```bash
python3 scripts/check_source_size.py
python3 scripts/check_source_size.py --staged
```

On Windows use `py` in place of `python3`. The staged check reads the Git index,
so unstaged edits cannot hide what is being committed. CI must also check the
whole tree independently. Local hooks can be bypassed and do not replace CI.
During hardening, a whole-tree failure identifies remaining decomposition work;
it is not grounds for a permanent allowlist.

## Architecture (the bar that matters)

Keep the layering and MVVM clean — this is what we look at in review:

- **Core is UI-free.** `Connect-A-Pic-Core` (domain, simulation, S-matrix) must not
  depend on Avalonia, ViewModels, or Views. UI/ViewModels live in `CAP.Avalonia`,
  data access in `CAP-DataAccess`.
- **MVVM** (CommunityToolkit): logic and state go in ViewModels
  (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`); Views only bind.
  No business logic in code-behind.
- One responsibility per class; add tests for new logic.
- **Cross-platform parity.** Lunima targets macOS, Linux, and Windows *together* — don't let
  one OS fall behind. Never call `Process.Start` directly or hardcode OS shell commands: use
  `ProcessLaunchFactory` for external tools (python/docker) and `IUrlLauncher` for opening
  URLs/files. Use `Path.Combine` and `InvariantCulture` for machine output; condition
  Windows-only `.csproj` properties; and keep the release matrix (Windows MSI + portable,
  Linux tarball, macOS `.dmg`) in sync. Details: [CLAUDE.md §1.2](CLAUDE.md).

Full rules: [CLAUDE.md](CLAUDE.md).

The canonical agent instructions are [CLAUDE.md](CLAUDE.md).
`.claude/CLAUDE.md` links there; Windows checkouts with `core.symlinks=false`
should read the root file directly instead of the Git symlink placeholder.
