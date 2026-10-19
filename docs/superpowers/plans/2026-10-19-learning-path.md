# Continuous learning path implementation plan

**Goal:** Integrate the approved seven-chapter learning path into the real Lunima desktop editor, preserving its existing visual language and action-based guides.

**Architecture:** A lightweight learning-path view model observes existing lesson lifecycle events. It persists earned chapter badges and the last chapter separately from project state. Existing lesson starters retain unsaved-change confirmation. A docked panel shows the complete path without covering placement targets. Manufacturing observes a real successful export.

**Tech stack:** C#, Avalonia, CommunityToolkit.Mvvm, existing localization/preferences and xUnit.

**Spec:** User-approved browser design and native-style integration in this conversation.

## Constraints and review focus
- Do not replace existing pointer demonstrations with text instructions.
- Award badges for real completion only; skipping and cancelled document changes earn nothing.
- All seven chapters remain accessible, with no forced automatic document replacement.
- Persist earned progress; repeated completion does not duplicate rewards.
- Use existing theme resources and translations; support a collapsed panel on narrow windows.
- Preserve export cancellation/failure and Python setup behavior.
- Serialize builds/tests across workers. Do not modify original working-copy changes.

## Tasks
- [x] Add LearningPathViewModel and chapter models, preference persistence, tests for completion, restart, cancelled start, next, and bounds.
- [x] Wire all existing actual-action lesson events and starters; expose course entry on Home; dock panel in editor.
- [x] Build native themed LearningPathPanel with chapter tree, active/completed states, progress, next, collapse/close and reward feedback.
- [x] Add manufacturing lesson, process/material explanation and successful-export observation, with cancellation/failure tests.
- [x] Run focused tests, render the actual Home/editor panel, independent review and fix findings.
- [x] Build and launch the actual desktop app from the isolated worktree for the user.

## Execution notes
Root implements course model/integration. Independent workers own only panel files and manufacturing lesson/export instrumentation. No simultaneous dotnet builds. Browser mockup is reference only; native resource tokens own appearance.

## Verification
150 focused lesson, Home, course-state and real-window UI tests passed. Desktop build: 0 warnings, 0 errors. Actual new CAP.Desktop.exe launched (PID26084), responsive Lunima window, executable path confirmed. Native screenshot automation unavailable (missing pipe); headless real MainWindow render inspected at 1440/1100px. Independent review findings on pending close and stale inflight exports fixed with regression coverage.
