# Testing Guide for Connect-A-PIC-Pro

This document explains how to run tests efficiently using xUnit v3's native CTRF JSON output.

## Quick Start

### Run All Tests (Recommended)

```bash
dotnet run --project UnitTests -- -ctrf test-results.ctrf.json
```

This produces a structured JSON file with all test results.

### Run Tests with Console Output

```bash
dotnet run --project UnitTests
```

## Why CTRF JSON?

**CTRF (Common Test Report Format)** is a standardized JSON format for test results.

### Benefits:

- **Structured**: Easy to parse and analyze programmatically
- **Compact**: ~400 tokens vs. ~2000 tokens for console text
- **Complete**: Includes test names, status, duration, errors, stack traces
- **Machine-readable**: Perfect for CI/CD, automation, and AI agents

### Example Output:

```json
{
  "results": {
    "summary": {
      "tests": 998,
      "passed": 971,
      "failed": 7,
      "skipped": 20
    },
    "tests": [
      {
        "name": "BuildGroupSMatrix_EmptyGroup_ReturnsNull",
        "status": "passed",
        "duration": 0.3651
      },
      {
        "name": "BuildGroupSMatrix_SupportsMultipleWavelengths",
        "status": "failed",
        "duration": 6,
        "message": "System.ArgumentOutOfRangeException: Index was out of range..."
      }
    ]
  }
}
```

## Alternative Test Output Formats

xUnit v3 supports multiple output formats:

### TRX XML (Visual Studio Test Results)

```bash
dotnet run --project UnitTests -- -trx test-results.trx
```

Good for Visual Studio integration and Azure DevOps.

### JUnit XML

```bash
dotnet run --project UnitTests -- -jUnit test-results.xml
```

Good for Jenkins and other CI systems.

### HTML Report

```bash
dotnet run --project UnitTests -- -html test-results.html
```

Good for human-readable reports.

### xUnit v2 XML

```bash
dotnet run --project UnitTests -- -xml test-results.xml
```

Legacy format for xUnit v2 compatibility.

## Advanced Usage

### Run Specific Tests

```bash
# Run a single test by name
dotnet run --project UnitTests -- -method "MethodName"

# Run all tests in a class
dotnet run --project UnitTests -- -class "ClassName"
```

### Parallel Execution

```bash
# Disable parallelization (useful for debugging)
dotnet run --project UnitTests -- -parallel none

# Aggressive parallelization
dotnet run --project UnitTests -- -parallelAlgorithm aggressive
```

### Verbose Output

```bash
# Show detailed progress
dotnet run --project UnitTests -- -reporter verbose

# JSON progress messages
dotnet run --project UnitTests -- -reporter json
```

### Multiple Output Formats

You can generate multiple reports in one run:

```bash
dotnet run --project UnitTests -- \
  -ctrf results.ctrf.json \
  -html results.html \
  -trx results.trx
```

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

## CI/CD Integration

### For AI Agents (Claude Code)

Use CTRF for token efficiency:

```bash
dotnet run --project UnitTests -- -ctrf test-results.ctrf.json
```

Then parse the JSON to get structured results.

### For GitHub Actions

```yaml
- name: Run Tests
  run: dotnet run --project UnitTests -- -ctrf test-results.ctrf.json -html test-report.html

- name: Upload Results
  uses: actions/upload-artifact@v3
  with:
    name: test-results
    path: |
      test-results.ctrf.json
      test-report.html
```

### For Azure DevOps

```yaml
- task: DotNetCoreCLI@2
  inputs:
    command: 'run'
    projects: 'UnitTests/UnitTests.csproj'
    arguments: '-- -trx test-results.trx'

- task: PublishTestResults@2
  inputs:
    testResultsFormat: 'VSTest'
    testResultsFiles: '**/test-results.trx'
```

## Troubleshooting

### CTRF Not Generated

**Problem:** Running `dotnet test -- --report-ctrf` doesn't create a file.

**Solution:** Use the xUnit v3 native runner instead:
```bash
dotnet run --project UnitTests -- -ctrf test-results.ctrf.json
```

**Why?** `dotnet test` uses VSTest/MTP v2 SDK layer, which doesn't support CTRF. The xUnit v3 runner has native CTRF support.

### Tests Fail to Run

**Problem:** `dotnet run --project UnitTests` fails with build errors.

**Solution:** Build first, then run:
```bash
dotnet build
dotnet run --project UnitTests --no-build -- -ctrf test-results.ctrf.json
```

### Output File Not Found

**Problem:** Can't find the generated report file.

**Check:** The file is created in the current working directory. Use absolute paths:
```bash
dotnet run --project UnitTests -- -ctrf /tmp/test-results.ctrf.json
```

## Technical Background

### xUnit v3 vs. VSTest

Connect-A-PIC-Pro uses **xUnit v3** with **Microsoft Testing Platform v2 (MTP v2)**.

| Tool | Test Runner | CTRF Support |
|------|-------------|--------------|
| `dotnet test` | VSTest or MTP v2 SDK | ❌ No native support |
| `dotnet run --project UnitTests` | xUnit v3 native | ✅ Full support |

### Why Not dotnet-test-mcp?

**dotnet-test-mcp** is an MCP (Model Context Protocol) server that wraps test execution for AI assistants. It requires:
- .NET 10 SDK
- Additional installation and configuration
- MCP-aware clients

**xUnit v3's native CTRF is simpler:**
- No extra tools needed
- Works with xUnit v3 out of the box
- Standard JSON format

Use dotnet-test-mcp only if you need advanced MCP features like test discovery and filtering via protocol messages.

## References

- [xUnit v3 Documentation](https://xunit.net/docs/getting-started/v3/getting-started)
- [Microsoft Testing Platform](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [CTRF Specification](https://ctrf.io/)
- [xUnit v3 Release Notes](https://xunit.net/releases/v3/3.0.0)
