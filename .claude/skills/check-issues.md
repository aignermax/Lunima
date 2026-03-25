# Check Issues Skill

This skill checks for open GitHub issues that need to be processed by the agent.

## Instructions

1. **List open issues** using the GitHub MCP server:
   - Use the `github_list_issues` tool
   - Filter for issues with the label `agent-task`
   - Exclude issues that already have a linked PR (check issue body for PR links)
   - Exclude issues with labels: `blocked`, `wontfix`, `duplicate`

2. **Prioritize issues**:
   - Sort by priority labels (if present): `priority:high`, `priority:medium`, `priority:low`
   - If no priority labels, sort by creation date (oldest first)
   - Prefer issues with label `good-first-issue` if available

3. **Return result**:
   - If issues found: Return the issue number and title of the highest priority issue
   - If no issues found: Return a message indicating no work is available

## Example Usage

When invoked, this skill should:
- Check the repository for open `agent-task` issues
- Return: "Found issue #123: Add dark mode support" (if work available)
- Or return: "No agent-task issues found" (if no work available)

## Integration

This skill is typically invoked automatically by the issue-worker agent in its continuous loop.
