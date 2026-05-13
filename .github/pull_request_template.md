## Summary

<!-- 1–3 bullet points describing what this PR does -->

## Related issue

Closes #

## Test plan

<!-- How was this tested? -->

- [ ] Build passes (`dotnet build` / `build_errors.py`)
- [ ] All tests pass (`python3 ~/.cap-tools/smart_test.py`)
- [ ] No new warnings introduced

## Architecture checklist

- [ ] Follows vertical-slice convention: new feature code lives in mirrored subfolders across layers (see CLAUDE.md §1.1)
- [ ] No cross-feature reach-in: feature code only references its own namespace or the shared kernel
- [ ] New DI registrations added to the feature's extension method in `CAP.Avalonia/DI/` (not directly in `App.axaml.cs`)
- [ ] No new file exceeds 250 lines (or grandfathered in `FileSizeLimitTests.cs` with an issue reference)

## Notes for reviewer

<!-- Anything else the reviewer should know: tricky edge cases, follow-up issues, etc. -->
