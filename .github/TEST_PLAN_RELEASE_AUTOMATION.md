# Test Plan: Release Automation Fixes (Issue #59)

## Overview

This test plan verifies the fixes for:
1. Correct tag version in releases (no more `0.1.0` duplicates)
2. Smart changelog generation with categorized commits

## Prerequisites

- Repository with GitHub Actions enabled
- Permissions to trigger workflows and create releases
- At least one previous release tag (e.g., `v0.3.1`)

## Test Cases

### Test Case 1: Normal Release Flow (Primary Path)

**Objective:** Verify the complete automated release process works end-to-end

**Steps:**
1. Go to Actions → "Version Change" workflow
2. Click "Run workflow"
3. Enter version: `0.4.0` (no `v` prefix)
4. Click "Run workflow"
5. Wait for PR to be created (~10 seconds)
6. Review the PR:
   - Title should be: `chore: update version to 0.4.0`
   - Body should explain the automated process
   - Files changed: `CAP.Desktop/CAP.Desktop.csproj`
   - Version in csproj should be `<Version>0.4.0</Version>`
7. Approve and merge the PR
8. Go to Actions and monitor workflows:
   - "Auto-Tag on Version PR Merge" should run (~5 seconds)
   - "Build & Publish" should trigger after tag is created (~3-5 minutes)
9. Go to Releases page
10. Verify the new release

**Expected Results:**
- ✅ PR created with correct version change
- ✅ Tag `v0.4.0` created after PR merge (not before)
- ✅ All tests pass
- ✅ Release created with:
  - **Tag:** `v0.4.0` (NOT `0.1.0` or `v0.1.0-build.X`)
  - **Title:** "Connect-A-PIC Pro v0.4.0"
  - **Changelog:** Categorized list of commits since `v0.3.1`
  - **Downloads:** Windows and Linux artifacts with correct version numbers
- ✅ No duplicate tags created

**Success Criteria:**
- Release uses correct tag version (`v0.4.0`)
- Changelog appears with at least one category
- Downloads have correct filenames (`Connect-A-PIC-Pro-0.4.0-win-x64.zip`)

---

### Test Case 2: Manual Tag Creation (Alternative Path)

**Objective:** Verify workflow works when tag is created manually

**Steps:**
1. Update version in csproj manually:
   ```bash
   # Edit CAP.Desktop/CAP.Desktop.csproj
   # Change <Version>0.4.0</Version> to <Version>0.4.1</Version>
   git add CAP.Desktop/CAP.Desktop.csproj
   git commit -m "chore: bump version to 0.4.1"
   git push origin main
   ```
2. Create and push tag:
   ```bash
   git tag v0.4.1
   git push origin v0.4.1
   ```
3. Go to Actions → "Build & Publish" workflow
4. Verify workflow is triggered by tag push
5. Monitor workflow execution
6. Check Releases page

**Expected Results:**
- ✅ Workflow triggered by tag `v0.4.1`
- ✅ Tests pass
- ✅ Release created with tag `v0.4.1`
- ✅ Changelog generated from `v0.4.0..v0.4.1`
- ✅ Version warning appears if tag doesn't match csproj (in workflow logs)

**Success Criteria:**
- Release uses the manually created tag (`v0.4.1`)
- No duplicate tags
- Changelog shows commits since `v0.4.0`

---

### Test Case 3: Workflow Dispatch (Dev Build)

**Objective:** Verify manual workflow trigger creates build tag correctly

**Steps:**
1. Go to Actions → "Build & Publish" workflow
2. Click "Run workflow"
3. Select branch: `main`
4. Click "Run workflow"
5. Monitor workflow execution
6. Check tags page
7. Check Releases page

**Expected Results:**
- ✅ Workflow runs successfully
- ✅ Build tag created: `v0.4.1-build.N` (where N is run number)
- ✅ Tests pass
- ✅ Release created with build tag
- ✅ Changelog generated since last release tag

**Success Criteria:**
- Build tag format is correct
- Release is marked appropriately (could be pre-release)
- No interference with release tags

---

### Test Case 4: Changelog Categorization

**Objective:** Verify commits are correctly categorized in changelog

**Setup:**
Create test commits with different prefixes:
```bash
git commit --allow-empty -m "feat: Add new feature X"
git commit --allow-empty -m "fix: Resolve bug Y"
git commit --allow-empty -m "refactor: Improve code Z"
git commit --allow-empty -m "docs: Update documentation"
```

**Steps:**
1. Create version bump PR (0.4.2)
2. Merge PR
3. Wait for release creation
4. Check release notes

**Expected Results:**
- ✅ "✨ New Features" section contains: `feat: Add new feature X`
- ✅ "🐛 Bug Fixes" section contains: `fix: Resolve bug Y`
- ✅ "🔧 Improvements" section contains: `refactor: Improve code Z`
- ✅ "📝 Other Changes" section contains: `docs: Update documentation`
- ✅ Commit count is accurate

**Success Criteria:**
- All commits appear in changelog
- Categories are correct
- Emoji icons display properly

---

### Test Case 5: Tag Mismatch Warning

**Objective:** Verify warning appears when tag doesn't match csproj version

**Steps:**
1. Keep csproj version at `0.4.2`
2. Manually create tag with different version:
   ```bash
   git tag v0.5.0
   git push origin v0.5.0
   ```
3. Go to Actions → "Build & Publish" workflow run
4. Check workflow logs in "Compute release info" step

**Expected Results:**
- ✅ Workflow runs successfully (doesn't fail)
- ✅ Warning message appears in logs:
  ```
  ::warning::Tag 'v0.5.0' does not match csproj version 'v0.4.2'
  ```
- ✅ Release still created with tag `v0.5.0`

**Success Criteria:**
- Warning is visible but not fatal
- Release still completes
- Tag version is used (not csproj version)

---

### Test Case 6: No Previous Tag (First Release)

**Objective:** Verify changelog generation works for first release

**Setup:**
- Test in a new repository or branch with no tags

**Steps:**
1. Create first release with version `0.1.0`
2. Follow normal release flow
3. Check release notes

**Expected Results:**
- ✅ Release created successfully
- ✅ Changelog shows commits from repository start
- ✅ Commit count displayed: "X commits in this release"
- ✅ No error about missing previous tag

**Success Criteria:**
- Workflow doesn't fail on missing previous tag
- Changelog contains recent commits (up to 20)

---

### Test Case 7: Duplicate Tag Prevention

**Objective:** Verify system prevents duplicate tags

**Steps:**
1. Manually create tag `v0.4.3`
2. Trigger version change workflow with version `0.4.3`
3. Merge the PR
4. Monitor auto-tag workflow

**Expected Results:**
- ✅ PR created and merged successfully
- ✅ Auto-tag workflow runs
- ✅ Workflow detects existing tag
- ✅ Workflow skips tag creation with message:
  ```
  Tag v0.4.3 already exists, skipping tag creation
  ```
- ✅ Build & Publish workflow doesn't trigger again

**Success Criteria:**
- No duplicate tags created
- Workflow completes successfully
- Clear log message about skipping

---

## Regression Tests

### Regression Test 1: Existing Releases Unchanged

**Objective:** Verify old releases are not affected

**Steps:**
1. Check existing releases (v0.3.1, v0.3.0, etc.)
2. Verify tags still exist
3. Verify downloads still work

**Expected Results:**
- ✅ All previous releases intact
- ✅ Tags unchanged
- ✅ Download links still work

---

### Regression Test 2: Other Workflows Unaffected

**Objective:** Verify other workflows still work

**Steps:**
1. Create a regular PR (not version change)
2. Push commits
3. Verify tests run
4. Merge PR
5. Verify no auto-tag is created

**Expected Results:**
- ✅ xUnit Tests workflow runs normally
- ✅ No auto-tag created for regular PRs
- ✅ No interference with development workflow

---

## Performance Tests

### Performance Test 1: Workflow Execution Time

**Objective:** Ensure workflows complete in reasonable time

**Expected Results:**
- ✅ Version Change workflow: < 30 seconds
- ✅ Auto-Tag workflow: < 15 seconds
- ✅ Build & Publish workflow: 3-5 minutes (depends on tests and build)

---

## Security Tests

### Security Test 1: Permissions

**Objective:** Verify workflows have minimal required permissions

**Steps:**
1. Review workflow YAML files
2. Check permissions blocks

**Expected Results:**
- ✅ `version_change.yaml`: Uses default `GITHUB_TOKEN`, no extra permissions
- ✅ `auto_tag_on_version_pr.yaml`: Only `contents: write` permission
- ✅ `Build_Exe.yaml`: Only `contents: write` permission in release job

---

### Security Test 2: No Secret Exposure

**Objective:** Ensure no secrets are logged

**Steps:**
1. Review workflow logs for all test cases
2. Check for exposed tokens or sensitive data

**Expected Results:**
- ✅ No GITHUB_TOKEN in logs
- ✅ No API keys or secrets visible
- ✅ Only expected output (versions, tags, filenames)

---

## Manual Verification Checklist

After running all automated tests, manually verify:

- [ ] GitHub Releases page shows all releases with correct tags
- [ ] Download links work (Windows and Linux)
- [ ] Changelog is user-friendly and readable
- [ ] No duplicate tags in repository
- [ ] Version in csproj matches latest release
- [ ] RELEASE.md documentation is accurate
- [ ] No broken workflows in Actions tab

---

## Rollback Plan

If tests fail:

1. **Revert workflow changes:**
   ```bash
   git revert <commit-hash>
   git push origin main
   ```

2. **Delete problematic tags:**
   ```bash
   git push origin :vX.X.X
   ```

3. **Manually create release:**
   - Follow instructions in RELEASE.md "Manual Release (Fallback)" section

---

## Sign-off

**Tested by:** _________________
**Date:** _________________
**Results:** Pass / Fail
**Notes:**
