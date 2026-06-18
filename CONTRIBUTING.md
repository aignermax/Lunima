# Contributing to Lunima

Thanks for contributing! This guide describes how we work so reviews stay fast
and `main` stays releasable.

## Branching & PR workflow

`main` is protected: **no direct pushes**. Every change lands via a pull request.

1. **Branch off `main`** with a descriptive name: `feat/...`, `fix/...`,
   `refactor/...`, `docs/...`.
2. **Keep the PR small and focused — one concern per PR.** Don't mix a refactor
   with a feature. A reviewer should be able to hold the whole diff in their head
   (rule of thumb: a handful of files, a few hundred lines). Large, stacked PRs
   are the main thing that makes review painful — split them.
3. **Open the PR** against `main`.
4. **Run the Claude PR-review skill** on the PR and **fix the findings**.
5. **Human review:** a maintainer checks that it works as a feature and that the
   architecture is sound.
6. On approval: **merge and delete the branch.**

> If you must build on not-yet-merged work, keep the stack short and set the PR's
> **base to the parent branch** (not `main`) so the diff shows only your delta —
> then rebase onto `main` as soon as the parent merges.

## What the protection rules enforce (`main`, future `dev`)

- A pull request with **at least 1 approving review**
- The **`🔍 xUnit Tests`** CI check must pass, and the branch must be **up to date**
- No force-pushes, no branch deletion

So CI green + one human approval are required before merge.

## Build & test

- Build: `dotnet build`
- **Run tests via `python3 tools/smart_test.py`** (compact, agent-friendly output).
  Avoid raw `dotnet test` — it produces 100k+ characters and overflows context.
  - All tests: `python3 tools/smart_test.py`
  - Pattern: `python3 tools/smart_test.py <Pattern>`
- Don't finish until **build and tests pass.**

## Code conventions

See **[CLAUDE.md](CLAUDE.md)** for the full guide (SOLID, ≤250 lines per *new*
file, one responsibility per class, MVVM with CommunityToolkit, DI in
`App.axaml.cs`, XML docs on public members, tests for new logic).

## Commit message prefixes

Prefix commits with a symbol indicating the nature of the change:

| Prefix | Meaning |
|--------|---------|
| `(+)`  | New functionality, backwards compatible |
| `(!)`  | New functionality, **not** backwards compatible (breaking) |
| `(-)`  | Removed functionality |
| `(=)`  | Bugfixes only |
| `(*)`  | Mixed fix (feature + fix) |
| `(~)`  | Refactor / change in how something works, not how it's used |
| `(c)`  | Comments / cosmetic / chores |

If a commit spans categories, pick the most significant one.

## AI-assisted development

Lunima is developed with heavy AI assistance. See [CLAUDE.md](CLAUDE.md) for
agent guidelines and the Python helper tools (`tools/smart_test.py`,
`tools/semantic_search.py`).
