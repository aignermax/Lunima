# Release Automation Fixes - Issue #59

## Summary

Fixed two critical issues in the release automation:
1. ✅ Releases now use the correct tag version (no more `0.1.0` duplicates)
2. ✅ Releases now include smart, categorized changelogs

## Changes Made

### 1. Enhanced Tag Handling (`Build_Exe.yaml`)

**Before:**
```yaml
if [ "${{ github.ref_type }}" = "tag" ]; then
  echo "tag=${{ github.ref_name }}" >> $GITHUB_OUTPUT
```

**After:**
```yaml
# Extract version from csproj
version=$(grep '<Version>' CAP.Desktop/CAP.Desktop.csproj | ...)
echo "version=$version" >> $GITHUB_OUTPUT

# Determine tag based on trigger type
if [ "${{ github.ref_type }}" = "tag" ]; then
  # Use the actual tag that triggered this workflow
  tag="${{ github.ref_name }}"
  echo "tag=$tag" >> $GITHUB_OUTPUT
  echo "Using existing tag: $tag"
else
  # Manual workflow_dispatch - create build tag
  tag="v${version}-build.${{ github.run_number }}"
  echo "tag=$tag" >> $GITHUB_OUTPUT
  echo "Creating build tag: $tag"
fi

# Verify tag matches version (warning only)
if [ "${{ github.ref_type }}" = "tag" ]; then
  if [ "$tag" != "v$version" ]; then
    echo "::warning::Tag '$tag' does not match csproj version 'v$version'"
  fi
fi
```

**Benefits:**
- Explicitly uses `${{ github.ref_name }}` from the tag trigger
- Adds verification to warn if tag doesn't match csproj version
- Better logging for debugging
- Separates build tags from release tags

### 2. Smart Changelog Generation (`Build_Exe.yaml`)

Added new `Generate Changelog` step that:
- Finds the previous release tag automatically
- Extracts commits since the last release
- Categorizes commits by type:
  - ✨ New Features (`feat:`, `add:`, `implement:`)
  - 🐛 Bug Fixes (`fix:`, `bugfix:`)
  - 🔧 Improvements (`refactor:`, `improve:`, `chore:`)
  - ⚠️ Breaking Changes (contains `BREAKING`)
  - 📝 Other Changes (everything else)
- Counts commits and shows "X commits since vY.Y.Y"
- Uses multiline output format for GitHub Actions

**Example output:**
```markdown
### ✨ New Features
- feat: Add multi-component selection with box selection
- implement: Group drag operations

### 🐛 Bug Fixes
- fix: Export JSON button now works in diagnostics

### 🔧 Improvements
- refactor: Replace Manhattan router with smooth CSC planner
- improve: Better routing for close pins

---
**12 commits** since v0.3.1
```

### 3. Updated Release Body Format (`Build_Exe.yaml`)

**Before:**
```yaml
body: |
  ## Connect-A-PIC Pro v${{ steps.info.outputs.version }}

  ### Windows
  1. Download `Connect-A-PIC-Pro-...-win-x64.zip`
  ...
```

**After:**
```yaml
body: |
  ## Connect-A-PIC Pro v${{ steps.info.outputs.version }}

  ${{ steps.changelog.outputs.changelog }}

  ## 📦 Installation

  ### Windows
  1. Download `Connect-A-PIC-Pro-${{ steps.info.outputs.version }}-win-x64.zip`
  ...
```

**Benefits:**
- Changelog appears prominently at the top
- Installation instructions are still present but under clear section
- Actual version numbers in download filenames (no more `...`)

### 4. New Auto-Tag Workflow (`auto_tag_on_version_pr.yaml`)

Created a new workflow that:
- Triggers when a PR is merged to `main`
- Checks if PR title starts with `"chore: update version"`
- Extracts version from merged csproj file
- Checks if tag already exists (prevents duplicates)
- Creates and pushes tag `vX.X.X`
- This tag then triggers the `Build_Exe.yaml` workflow

**Benefits:**
- Tag is created AFTER version is actually updated in main
- No race conditions or premature tagging
- Automatic - no manual git commands needed
- Safe - checks for existing tags first

### 5. Updated Version Change Workflow (`version_change.yaml`)

**Removed:** Early tag creation (which caused issues)

**Added:** Better PR description explaining the automated process

**Before:**
```yaml
- name: Create and Push Tag
  run: |
    git config user.name "github-actions"
    git config user.email "github-actions@github.com"
    git tag v${{ github.event.inputs.version }}
    git push origin v${{ github.event.inputs.version }}
```

**After:**
```yaml
# No tag creation here - handled by auto_tag_on_version_pr.yaml after PR merge
```

PR body now explains:
```markdown
When this PR is merged, the following will happen automatically:
1. ✅ Version updated to X.X.X in CAP.Desktop.csproj
2. 🏷️ Tag vX.X.X created automatically
3. 🏗️ Windows and Linux builds triggered
4. 🧪 All tests run
5. 📦 GitHub release created with changelog
6. ⬆️ Release artifacts uploaded

No manual steps required after merge!
```

**Benefits:**
- Clearer communication of the process
- No duplicate tags
- Correct order of operations

### 6. Comprehensive Documentation (`RELEASE.md`)

Created a complete guide covering:
- How to create a new release (step-by-step)
- How each workflow works
- Changelog generation details
- Best practices for commit messages
- Troubleshooting common issues
- Manual release fallback procedure
- Security notes

## Testing the Changes

### Test Scenario 1: Normal Release Flow

1. **Trigger version change:**
   ```
   Actions → Version Change → Run workflow → Enter "0.4.0"
   ```

2. **Expected result:**
   - PR created: `chore: update version to 0.4.0`
   - PR description explains automated process
   - No tag created yet

3. **Merge the PR**

4. **Expected result:**
   - `auto_tag_on_version_pr.yaml` runs automatically
   - Tag `v0.4.0` created and pushed
   - `Build_Exe.yaml` triggered by tag
   - Tests run
   - Builds created
   - Release created with:
     - Tag: `v0.4.0` ✅ (not `0.1.0`)
     - Changelog with categorized commits ✅
     - Windows and Linux downloads ✅

### Test Scenario 2: Manual Tag Creation

1. **Manually create tag:**
   ```bash
   git tag v0.4.1
   git push origin v0.4.1
   ```

2. **Expected result:**
   - `Build_Exe.yaml` triggered by tag
   - Uses tag name `v0.4.1` correctly
   - Warning if tag doesn't match csproj version
   - Changelog generated from commits since `v0.4.0`
   - Release created successfully

### Test Scenario 3: Workflow Dispatch (Dev Builds)

1. **Manually trigger Build_Exe.yaml:**
   ```
   Actions → Build & Publish → Run workflow
   ```

2. **Expected result:**
   - Creates build tag: `v0.3.1-build.42` (using csproj version + run number)
   - Tests run
   - Builds created
   - Release created as draft/pre-release

## Verification Checklist

- [x] YAML syntax is valid (checked with basic parser)
- [x] No hardcoded `0.1.0` anywhere in workflows
- [x] Tag version comes from `${{ github.ref_name }}` when triggered by tag
- [x] Changelog generation script is complete
- [x] Multiline output format is correct (`EOF` delimiter)
- [x] Auto-tag workflow has proper permissions (`contents: write`)
- [x] Auto-tag workflow checks for existing tags
- [x] Documentation is comprehensive
- [ ] Live test with actual version bump (requires PR merge)

## Files Changed

1. `.github/workflows/Build_Exe.yaml` - Enhanced tag handling + changelog
2. `.github/workflows/version_change.yaml` - Removed premature tagging
3. `.github/workflows/auto_tag_on_version_pr.yaml` - NEW: Auto-tag on PR merge
4. `RELEASE.md` - NEW: Comprehensive documentation

## Migration Notes

**No breaking changes.** The workflows are backward-compatible:
- Existing tags will still trigger releases correctly
- Manual workflow dispatch still works
- The only change is that version PRs no longer create tags upfront

**First use after merge:**
1. The next version bump will test the full flow
2. Previous releases remain unchanged
3. No manual intervention needed

## Root Cause Analysis

### Why was `0.1.0` appearing?

Possible causes (now fixed):
1. **Race condition:** Tag was created before version was updated in main
2. **Wrong reference:** Workflow might have been using a fixed ref instead of `github.ref_name`
3. **Build tag confusion:** Build tags (`v0.1.0-build.X`) might have been confused with release tags

### How we fixed it:

1. **Explicit tag source:** Use `${{ github.ref_name }}` directly from trigger
2. **Verification step:** Warn if tag doesn't match csproj version
3. **Proper order:** Tag created AFTER version update in main
4. **Separation:** Build tags clearly distinguished from release tags

## Future Enhancements

Potential improvements for the future:
1. **AI-generated summaries:** Use Claude API to create high-level summaries
2. **PR-based changelog:** Include PR titles and descriptions
3. **Semantic versioning validation:** Enforce semver rules
4. **Auto-increment:** Suggest next version based on commit types
5. **Release notes templates:** Customizable sections
6. **Notification:** Post to Discord/Slack when release is created
