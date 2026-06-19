# Contributing to Lunima

`main` is protected — all changes go through a pull request.

## Workflow

1. Branch off `main` (`feat/...`, `fix/...`, `docs/...`).
2. Keep the PR **small and focused — one concern per PR.** Big, mixed PRs are the
   main thing that makes review painful; split them.
3. Open the PR. To merge you need the **`🔍 xUnit Tests`** check green and
   **1 approval**.
4. Merge, then delete the branch.

## Build & test

```bash
dotnet build
python3 tools/smart_test.py        # run tests (avoid raw `dotnet test` — it floods the console)
```

## Architecture (the bar that matters)

Keep the layering and MVVM clean — this is what we look at in review:

- **Core is UI-free.** `Connect-A-Pic-Core` (domain, simulation, S-matrix) must not
  depend on Avalonia, ViewModels, or Views. UI/ViewModels live in `CAP.Avalonia`,
  data access in `CAP-DataAccess`.
- **MVVM** (CommunityToolkit): logic and state go in ViewModels
  (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`); Views only bind.
  No business logic in code-behind.
- One responsibility per class; add tests for new logic.

Full rules: [CLAUDE.md](CLAUDE.md).
