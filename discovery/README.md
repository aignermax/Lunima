# Product discovery

This folder is written by the weekly product-discovery run of the Lunima team agent
(scout → strategist → critic) and reviewed by the maintainer. It is **not** published on
lunima.org: GitHub Pages serves `docs/` only.

| Path | What | Who writes it |
|---|---|---|
| `HYPOTHESES.md` | competing strategic hypotheses with status and evidence | strategist (never deletes, only drops with a reason) |
| `evidence/<week>.md` | evidence entries with provenance and level E0–E3 | scout |
| `decisions/DR-NNNN-*.md` | Decision Records incl. the critic's verdict | strategist + critic |
| `experiments/X-*.md` | pre-registered experiments; results are appended | strategist |
| `campaigns/C-*.md` | paid-campaign plans (authorised budget: maintainer only, default 0) | strategist |
| `pages/<slug>/page.json` | landing-page drafts (rendered as local previews only) | strategist |
| `interviews/I-NNNN.md` | anonymised interview summaries | maintainer |
| `pilot/` | how pilot users are onboarded and how their feedback is recorded | maintainer |
| `runs/<week>.json` | progress record of a weekly run | agent |

## Evidence levels

| Level | Meaning | Classes |
|---|---|---|
| E0 | no external demand evidence | `agent-hypothesis`, `internal-test`, `simulated` |
| E1 | public, external, indirect | `external` (forum/issue posts, papers, competitor docs) |
| E2 | direct observation of a target user | `user-feedback` (interview summary, a user's own issue, metrics) |
| E3 | behavioural commitment | `user-commitment` (uses it on own design, pilot sign-up, LOI, payment) |

A Decision Record may recommend `pursue` only with at least one cited E2+ entry.

## How the maintainer decides

- **Accept Decision Records:** mark the weekly draft PR *Ready for review* and merge it.
  Before merging, reject single DRs with a PR comment `/reject DR-0003 <reason>`.
  Optional: `review-minutes: 15` in a comment records your review time.
- **Approvals** (issues labelled `discovery-approval`): add `approved` or `declined`.
  No label means not approved. Nothing external happens without it.
- **Interviews:** run them yourself, keep raw notes private, commit an anonymised summary
  from `interviews/TEMPLATE.md`.
- **Publish an experiment page:** `lunima-team-agent experiment publish <clone> <slug>
  --approved-by <you> --contact <address>`, review the diff, merge to `main`.
