# Export warning verification

Windows x64, 2026-11-16, Release publish of the issue branch (version 0.23.0).
The published bundle was copied outside the checkout with isolated preferences.
Both runs loaded its shipped `EBeam Add-Drop Ring.lun` through `agent call export_gds`.

| Python environment | Result |
| --- | --- |
| Nazca 0.6.1, no klayout/SiEPIC | 8,778-byte GDS, three warnings, explicit warning status |
| Nazca 0.6.1 with klayout, siepic_ebeam_pdk, SiEPIC | 138,434-byte GDS, real coupler hierarchy, all six waveguides carry 1/99, no warnings |

[Captured JSON and cell inspection](packaged-export.json) records both outcomes.
The inspector uses the full Python environment to read both GDS artifacts; it does
not run the export in that environment for the placeholder case.

Reproduce with:

```text
python scripts/verify_gds_export_warnings.py <published-executable> <nazca-only-python> <siepic-python>
```

The existing status bar displays the localized corrective action, while the error
console retains the original script diagnostics. This screenshot comes from the
headless desktop workflow regression with a controlled export result; the separate
packaged journey above verifies actual Python execution and GDS geometry.

![Existing export status with the placeholder warning](placeholder-warning.png)

Capture again by setting `UI_SHOT_DIR` to this directory and running
`python scripts/run_tests.py GdsExportWarningJourneyTests`.
The corrective action is provided in English, German, Spanish, Japanese and Chinese.
No new UI surface was introduced.

Verification was repeated against the existing Windows publish: both packaged
journeys and geometry assertions passed. The desktop build completed with zero
warnings/errors; 42 focused export and warning tests passed without skips using
`scripts/run_tests.py`. Coverage includes exit zero without a GDS artifact, which
remains a failure while retaining script warnings. The test project still emits
pre-existing warnings outside the changed tests. The source-size guard passed.

Independent customer acceptance is unavailable. macOS/Linux and MSI installation
were not exercised locally; the Windows published executable was exercised directly.

## QA retry (2026-11-16)

The reported QA failure was a host memory guard abort (1,907 MB available,
2,048 MB required), not a failing assertion. The guard was not weakened.
The requested `tools/build_errors.py` and `tools/smart_test.py` are absent;
both commands were attempted and reported missing files. Verification used the
checked-in helper required by `CLAUDE.md`, with .NET SDK 10.0.400 on Windows.

- `python -m unittest scripts.tests.test_run_tests`: 8 passed. Three new
  subprocess tests pin nonzero-exit propagation even after a passing summary,
  rejection of zero-exit runs without executed tests, and genuine passing runs.
  These simulate return codes and output without exhausting host memory.
- `python scripts/run_tests.py --filter 'FullyQualifiedName~GdsExportWarning|FullyQualifiedName~ExportWarningMessages|FullyQualifiedName~GdsExportServiceTests|FullyQualifiedName~GdsExportContractFlowTests'`:
  build succeeded, 18 passed, zero failed or skipped.
- `python scripts/run_tests.py`: build succeeded, but the full default suite
  did not complete. After several minutes without progress, `dotnet-stack report`
  showed `ManagedPythonProvisionerTests.BrokenExecutableFile_IsNotMistakenForUsableRuntime`
  blocked in `WindowsProcessLifetime.StartSuspended`. Only this run's process tree
  was terminated. This is not a full-suite pass or a reproduction of the memory
  guard abort; the export-focused run above completed afterwards.

Local diagnostic logs are in `TestResults/local/` (ignored):
`tests-20261116-151652-42456.log`, `qa-retry-stacks.txt`, and
`tests-20261116-152355-8448.log`. Packaged journeys and other operating systems
were not rerun for this test-only retry. Full-suite QA remains required.
