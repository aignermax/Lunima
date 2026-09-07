# Release Checklist

Lines are tagged `auto:` (enforced by CI, no human step needed) or
`manual:` (run by hand before tagging the release).

- auto: Golden-design regression gate — every shipped example in
  `examples/examples.json` loads, re-routes, passes DRC-lite, and matches its
  pinned simulation reference (`UnitTests/Regression/GoldenDesignTests.cs`,
  from #1149).
- auto: Full test suite passes on the release tag build (`.github/workflows/Build_Exe.yaml`).
- manual: MSI install + launch on a fresh machine; FDTD solver availability
  is detected and a small simulation runs end-to-end (from #590).
- manual: Verify the GitHub release page shows correct tag, changelog, and
  Windows/Linux/macOS download links.
