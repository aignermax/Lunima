# Process Issue Skill

This skill implements the complete workflow for processing a GitHub issue from analysis to PR creation.

## Prerequisites

Before starting, ensure you have:
- Issue number to process
- Repository is cloned locally
- CLAUDE.md file is available in the repository

## Workflow Steps

### 1. Understand the Issue

**Use GitHub MCP:**
- Call `github_get_issue` with the issue number
- Read the issue title, description, and comments
- Identify acceptance criteria and requirements

**Read Architecture Rules:**
- Use Read tool to load `CLAUDE.md` from the repository
- Pay special attention to the **Vertical Slice Requirement**
- Note: Every feature MUST include Core + ViewModel + View + Tests

**Search Relevant Code:**
- Use OpenViking MCP for semantic code search
- Find related components, view models, and tests
- Understand existing patterns and conventions

### 2. Plan the Solution

**Create Implementation Plan:**
- Break down into vertical slice layers:
  1. Core logic (new classes in `Connect-A-Pic-Core/`)
  2. ViewModel (new or updated in `CAP.Avalonia/ViewModels/`)
  3. View/AXAML (UI in `CAP.Avalonia/Views/` or `MainWindow.axaml`)
  4. DI wiring (register services in `App.axaml.cs`)
  5. Unit tests (xUnit in `UnitTests/`)
  6. Integration tests (Core + ViewModel in `UnitTests/`)

**Verify Compliance:**
- Each new file must be ≤250 lines
- Follow SOLID principles
- No God classes
- Only create interfaces when multiple implementations exist

### 3. Implement the Solution

**Create Core Logic:**
- Use Write/Edit tools to create new classes in `Connect-A-Pic-Core/`
- Follow C# naming conventions (PascalCase for public, _camelCase for private)
- Add XML documentation for all public members
- Keep classes focused (single responsibility)

**Create/Update ViewModel:**
- Inherit from `ObservableObject`
- Use `[ObservableProperty]` for bindable properties
- Use `[RelayCommand]` for user actions
- Register in DI container if new ViewModel

**Create/Update View:**
- Add UI panel in appropriate location
- Use `x:DataType` for compiled bindings
- Follow existing MainWindow layout patterns
- Add to Right panel as collapsible section

**Write Tests:**
- Create unit tests for core logic (xUnit + Shouldly)
- Create integration tests for Core + ViewModel interaction
- Ensure tests are independent and deterministic
- Cover edge cases and failure scenarios

### 4. Build and Test

**Run Build:**
- Use Bash tool: `dotnet build`
- Fix any compilation errors
- Ensure no new warnings

**Run Tests:**
- Use Bash tool: `dotnet test`
- Fix any failing tests
- Ensure all tests pass

**Iterate if Needed:**
- If build/tests fail, analyze errors and fix
- Repeat until all checks pass

### 5. Create Pull Request

**Stage and Commit Changes:**
- Use Bash tool: `git add <files>`
- Create commit with structured message:
  ```
  <type>: <summary>

  <detailed description>

  Fixes #<issue-number>

  🤖 Generated with Claude Code

  Co-Authored-By: Claude <noreply@anthropic.com>
  ```

**Push Branch:**
- Use Bash tool: `git push -u origin <branch-name>`

**Create PR using GitHub MCP:**
- Call `github_create_pull_request` with:
  - Title: Brief summary of changes
  - Body: Detailed description including:
    - Summary of implementation
    - Vertical slice layers completed
    - Test coverage
    - Link to issue: "Fixes #<issue-number>"
  - Base: `main` (or appropriate base branch)
  - Head: Current branch name

**Link PR to Issue:**
- The "Fixes #<issue-number>" in PR body will auto-link
- Verify PR was created successfully

### 6. Clean Up

**Log Progress:**
- Output summary of work completed
- Include PR URL
- Include time/token statistics if available

**Update Session State:**
- Mark issue as completed
- Clean up temporary files if any

## Error Handling

If any step fails:
1. Analyze the error message carefully
2. Attempt to fix the issue
3. If unable to resolve after 2 attempts, add a comment to the issue explaining the blocker
4. Move on to check for next issue

## Success Criteria

The skill is successful when:
- ✅ All build and tests pass
- ✅ PR is created and linked to issue
- ✅ Implementation follows CLAUDE.md rules
- ✅ Complete vertical slice is delivered (Core + ViewModel + View + Tests)

## Example Output

```
✅ Successfully processed issue #123: Add dark mode support

Implementation:
- Core: DarkModeManager.cs (127 lines)
- ViewModel: ThemeViewModel.cs (89 lines)
- View: Added theme toggle in MainWindow.axaml
- Tests: DarkModeManagerTests.cs (12 tests, all passing)

Build: ✅ Success
Tests: ✅ 12/12 passing

PR: https://github.com/aignermax/Connect-A-PIC-Pro/pull/263
```
