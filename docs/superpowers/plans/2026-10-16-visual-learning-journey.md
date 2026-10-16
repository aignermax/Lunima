# Visual Learning Journey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Teach real circuit construction through animated demonstrations at the controls being used.

**Architecture:** Keep observable action predicates in the onboarding view model, target resolution and pointer rendering in the existing tour overlay, and persistence/start decisions in main-view-model onboarding wiring. Reuse the real component library and canvas interaction state.

**Tech Stack:** C#, Avalonia, CommunityToolkit.Mvvm, xUnit/Avalonia.Headless.

**Spec:** ../specs/2026-10-16-visual-learning-journey.md

## Global Constraints

- No synthetic user input; ordinary editing stays available.
- No Python dependency; startup installation is a separate feature.
- All local builds/tests serialized with the other worktrees.
- Preserve unsaved work and explicit dismissal; all supported locales get copy.

## Review Focus

- Hidden/detached overlays must stop work and release target references.
- Resize, scrolling and replacement of a target must resolve the current visible target.
- A die or a single component must not satisfy connection prerequisites.
- Undo/deletion must not leave a pointer aiming at a nonexistent pin.
- Closing guidance must remain possible at small window sizes and with German copy.

### Task 1: Overlay lifecycle and visual demonstration

**Files:** `CAP.Avalonia/Controls/TourAnchor/TourAnchorOverlay.axaml(.cs)`; new `UnitTests/UI/VisualGuideOverlayTests.cs`.

**Interfaces:** Preserve Card and TargetName; add opt-in demonstration and precise target-point properties without changing existing tour callers.

- [ ] Add failing lifecycle tests for hidden, ancestor-hidden and detached overlays; show/reattach must resume.
- [ ] Run the focused tests through smart_test and observe the expected failures.
- [ ] Stop inactive polling, cache a valid target, invalidate on target changes/detach; preserve the existing hit-test contract.
- [ ] Add failing tests for a noninteractive ghost pointer at an explicit target point, absent-target behavior, and reduced motion.
- [ ] Implement pointer approach/click loop with cancellation and bounded layout; no input dispatch.
- [ ] Verify overlay and existing tour anchor/hit-test tests; commit.

### Task 2: Build-by-doing optical chapter

**Files:** `CAP.Avalonia/ViewModels/Onboarding/FirstStepsTutorial/TutorialViewModel.cs`, `TutorialStep.cs`, `CAP.Avalonia/Views/Panels/FirstStepsTutorialPanel.axaml`, main-window tour bindings, localization resources, onboarding tests.

**Interfaces:** Current action supplies target control plus optional canvas/library target; progress comes from live canvas/interaction observations.

- [ ] Add regression tests: die alone and one component cannot advance to connection; clicking Next cannot complete an unperformed action; undo/deletion recovers the prerequisite.
- [ ] Verify RED; implement select/place/select/place/connect/simulate stages using compatible real components and actual pin locations.
- [ ] Replace the paragraph card with short adjacent cue, dismiss and optional details; show brief success feedback.
- [ ] Verify the real library/canvas flow in English and German, including missing targets and small windows; inspect rendered screenshots and commit.

### Task 3: First-use entry and coherent journeys

**Files:** new main-view-model onboarding partial, existing Home/onboarding wiring, user preferences, logic/system tour presentation, UI flow tests.

- [ ] Add failing tests for first-use entry, remembered dismissal, resume, existing-project/unsaved-work preservation and background-Python coexistence.
- [ ] Implement default-on contextual entry and explicit resume; reuse visual demonstrations in logic/system action steps.
- [ ] Verify novice journeys by actual actions, inspect screenshots, obtain independent review and fix findings.
- [ ] Integrate with reviewed bootstrap/updater/routing branches, run complete CI and desktop validation before release.

## Execution record

- Initial inspection: current tour accepts one component then asks to connect; hidden overlay timers remain running. No implementation changes yet.
- User explicitly requested autonomous completion; execute inline without another design approval round.
