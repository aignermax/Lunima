#!/bin/bash
# Rebase all agent PRs on main

PRS=(
  "78:agent/issue-62-1773176489:62"  # Locked elements
  "77:agent/issue-66-1773175935:66"  # Grating Coupler position
  "76:agent/issue-67-1773175312:67"  # Straight Waveguide size
  "75:agent/issue-68-1773174336:68"  # Demo Grating Coupler rotation
  "74:agent/issue-69-1773173444:69"  # MMI 2x2 length
)

for pr_data in "${PRS[@]}"; do
  IFS=':' read -r pr_num branch issue_num <<< "$pr_data"
  
  echo "========================================="
  echo "Processing PR #$pr_num (Issue #$issue_num)"
  echo "========================================="
  
  # Reopen PR and issue
  gh pr reopen $pr_num
  gh issue reopen $issue_num
  
  # Fetch and checkout branch
  git fetch origin $branch
  git checkout -b temp-pr-$pr_num origin/$branch
  
  # Rebase on main
  if git rebase origin/main; then
    echo "✅ Rebase successful for PR #$pr_num"
    
    # Force push
    git push origin temp-pr-$pr_num:$branch --force
    echo "✅ Pushed rebased branch for PR #$pr_num"
  else
    echo "❌ Rebase failed for PR #$pr_num - conflicts need manual resolution"
    git rebase --abort
  fi
  
  # Cleanup
  git checkout main
  git branch -D temp-pr-$pr_num
  
  echo ""
done

echo "Done! Check GitHub Actions for CI status."
