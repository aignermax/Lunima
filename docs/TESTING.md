# Lunima testing guide

## Runner and SDK

`UnitTests/UnitTests.csproj` targets .NET 10 and references xUnit **2.4.2**,
`Microsoft.NET.Test.Sdk` 17.11.1 and the Visual Studio adapter. Tests run through
**VSTest**. This is a test library, not an executable xUnit v3 project; the old
`dotnet run --project UnitTests` and native CTRF instructions do not apply.
`global.json` is the SDK selection authority; there is no MTP runner configuration.
Install the SDK selected by that file and Python 3 for the checked-in helpers.

## Safe local entry point

Run from the repository root. Use one local .NET test run at a time.

```powershell
# Windows
py scripts/run_tests.py
py scripts/run_tests.py ParameterSweeperTests
```

```bash
# Linux/macOS
python3 scripts/run_tests.py
python3 scripts/run_tests.py ParameterSweeperTests
```

The helper targets `UnitTests/UnitTests.csproj`, defaults to
`Category!=Slow&Category!=Heavy`, and sets `DOTNET_PROCESSOR_COUNT=2` for the child
process, limits the default run to 15 minutes, and prevents overlapping helper
runs across worktrees with a shared lock. A positional argument matches `FullyQualifiedName` by substring while
retaining those exclusions. It writes detailed output to `TestResults/local/` and
prints its location; inspect that log when a run fails. Test collections run
serially as configured by `UnitTests/xunit.runner.json` because Avalonia headless
state is process-wide. Do not start competing test hosts in the same worktree.
Use `--timeout <seconds>` to change the overall timeout for a selected run.

Use `--filter` when you need a VSTest expression rather than a name substring.
The helper still appends the Slow/Heavy exclusions:

```bash
python3 scripts/run_tests.py --filter 'FullyQualifiedName~Routing&FullyQualifiedName!~Export'
```

On Windows, use `py` with the same arguments. Slow/Heavy suites normally run in CI;
an explicitly required local investigation should select the smallest relevant
set, add `--include-expensive`, and account for its memory and external-tool
requirements. That option requires a specific selection and removes the default
category exclusions. Passing the default
local filter is not evidence that Slow/Heavy or other platforms passed.

The processor setting reduces concurrency; it is not a CPU or memory quota.
`scripts/throttle.sh` can additionally use systemd resource limits where available,
but its fallback is unthrottled, including on systems without systemd. Do not claim
that Windows or macOS runs have a Linux cgroup memory cap.

## Results and external tools

CI uses VSTest TRX reports and per-shard logs, uploaded as `testresults-*` artifacts.
The local helper prints the path to its ignored `TestResults/local/` log. Report the exact selection, outcome
and skipped prerequisites when recording verification; do not infer success from
a build or from test discovery alone.

Some integration tests need Python packages, GDS tools or solver runtimes. CI setup
is defined in `.github/actions/setup-test-tools/`; use the relevant fixture's
requirements locally. `LUNIMA_TEST_PYTHON3` selects the interpreter used by tests.
Headless UI coverage does not replace native Windows/macOS/Linux desktop checks.

## CI test pipeline

`.github/workflows/xUnitTests.yaml` runs the suite on GitHub Actions in four parts:

| Job | What it does |
|-----|--------------|
| `🧭 Select test shards` | Decides which shards this run needs (`scripts/ci/select_test_shards.py`). |
| `🧪 <shard>` | One job per shard on its own runner: build, install the tools the shard needs, one `dotnet test --filter` process. |
| `🧮 Shard partition check` | Lists the tests each filter selects and fails unless every test is in exactly one shard (`scripts/ci/check_test_shards.py`). |
| `🔍 xUnit Tests` | The required status check. Green when every selected shard and the partition check passed, or when the change needed no tests. |

**Shards** are defined once in `scripts/ci/test-shards.json`: `fast-*` (`Category!=Slow`) and
`slow-*` (`Category=Slow`), split by namespace. A new top-level namespace automatically falls
into `fast-rest` / `slow-rest`. Slow UI classes (`UnitTests.UI.*` with `Category=Slow`) always
run alone in `slow-ui` — mixing them with other classes in one process deadlocked the Linux
runner. Inside a shard, tests run serially (`xunit.runner.json`). To check the partition
locally after a build: `python3 scripts/ci/check_test_shards.py` (it only lists tests, it does
not run them).

**Heavy tests.** Tests that take ~25 s or more carry `[Trait("Category", "Heavy")]` in addition
to their other traits. Pull requests skip them (`&Category!=Heavy` is appended to every
filter). Pushes to `dev` and `main`, release tags (also `Build_Exe.yaml`), the nightly
schedule (02:30 UTC, on `dev`) and manual `workflow_dispatch` runs execute the full suite.
Tag a new test Heavy when its TRX duration is ≥ ~25 s (`scripts/report_slowest_tests.py`).

**Test impact selection on PRs.** The changed paths (`git diff HEAD^1 HEAD` of the PR merge
commit) decide what runs:

- *Docs-only* paths (`docs/**`, root `*.md`, `.claude/**`, `.github/*.md`, issue templates,
  `Makefile`, `.agent.toml`, `run.sh`, `LICENSE`, `.gitignore`) need no tests, unless a test
  source names the file as a string literal, in which case everything runs. A PR that only
  touches docs-only paths gets a green check without running any shard.
- *Narrow rules* (`narrowRules` in `test-shards.json`) run a subset. Today there is one: other
  workflow files (e.g. `Build_Exe.yaml`) run only `fast-rest`, where the tests that read
  workflow files live. The partition check fails if that stops being true.
- *Everything else* runs every shard. That includes all code, `UnitTests/**`, `*.csproj`,
  `Directory.Build.*`, `examples/**`, PDK JSON files, localization JSON, `scripts/**` and
  this workflow. All test code sits in one assembly that references every production
  project, so a code change can affect any shard.

When in doubt the selection runs everything. Each push to `dev` runs the full suite
including Heavy, so anything a PR run skipped is caught right after merge.
