# Lunima release process

## Prepare and review

1. Review feature PRs, fix findings, and merge passing changes into `dev`.
2. Update `<Version>` in `CAP.Desktop/CAP.Desktop.csproj` on a branch from `dev`. Review and merge that PR into `dev`.
3. Open `dev` → `main` with the title `chore: update version to X.Y.Z`. Review the final diff and wait for CI before merging.
4. Confirm that `main` contains the intended version and commit. The website deploys from `main` too.

The optional `version_change.yaml` workflow creates a version-change PR. Check its base branch before merging; it does not replace reviewing the complete release batch.

## Tag and build

`auto_tag_on_version_pr.yaml` runs when a PR with the title prefix above is merged into `main`. It reads the project version, creates `vX.Y.Z` if absent, and explicitly dispatches `Build_Exe.yaml` for the merge commit. Do not also push a manual tag or dispatch a second build.

`Build_Exe.yaml` runs tests (including Heavy tests), then packages Windows, Linux, and both macOS architectures. The release is published only after the required jobs succeed. Build duration depends on the runners and test workload; follow the actual Actions run.

If a release PR used a different title, verify the intended `main` commit/version and existing tags before creating a tag manually. A `v*` tag push also triggers the release workflow. If a build fails, inspect and fix the failure; do not delete or move a published version tag to conceal it. Re-run a transient failure on the same commit, or release a new version for code changes.

## Verify the seven downloads

For version `X.Y.Z`, the published release must contain non-empty assets:

- `Lunima-Setup-X.Y.Z.msi`
- `Lunima-X.Y.Z-win-x64.zip`
- `Lunima-X.Y.Z-linux-x64.tar.gz`
- `Lunima-X.Y.Z-osx-arm64.dmg`
- `Lunima-X.Y.Z-osx-x64.dmg`
- `Lunima-X.Y.Z-osx-arm64.zip`
- `Lunima-X.Y.Z-osx-x64.zip`

Confirm the tag points to the intended release commit, the build tests passed, download links work, and the website offers the matching version. Use [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md) for behavior checks. Record what was actually tested; a headless UI test is not a native desktop smoke test.

After the downloads are published, update the static version labels and fallback asset links in `docs/index.html` to the released version. Verify them with the GitHub API unavailable as well as with the live latest-release response; an API failure must not send visitors to an older installer.

## Installation and first start

The Windows MSI is framework-dependent and checks for the compatible .NET 10 x64 runtime. Portable builds include .NET. macOS packages are unsigned; the release page documents first-launch steps.

Python and its preview/export packages are not bundled into the MSI. First desktop launch prepares the managed environment in the background. Editing and exploration remain available while it installs; the status indicator exposes progress, retry, and failure details. A healthy explicit external interpreter is preserved. Validate fresh provisioning and reuse separately, without changing a developer's personal Python installation.

For updates of a recognized Windows MSI installation, verify that the unsaved-work prompt is respected, the installer keeps the registered directory, and Lunima relaunches only after success. Portable copies retain the manual installation path. Clean-machine installation and native update behavior still need platform smoke tests in addition to automated command/fixture checks.

The first upgrade from 0.18.1 still uses the updater already installed in that version, including its existing MSI wizard. The simplified update flow is available for subsequent updates initiated by 0.18.2 or later; publishing a new installer cannot retroactively replace the running old updater.
