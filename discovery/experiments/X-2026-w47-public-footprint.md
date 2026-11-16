# X-2026-w47-public-footprint: Does anyone outside the maintainer, the maintainer's workplace and the agents leave public traces of using Lunima?
- status: proposed
- method: desk-research
- autonomy: autonomous
- assumption: Lunima already has users outside the maintainer, the maintainer's workplace and the agents (the "current users" in H0's riskiest assumption).
- hypotheses: H0, H7
- budget: 0 USD, about 30 min agent time (GitHub API, read-only) plus 5 min maintainer time; no contact with anyone

## Pre-registration
- success: in the window 2026-05-16 to 2026-11-16, at least 3 distinct GitHub accounts outside the excluded set opened, or commented on, an issue, discussion or pull request in aignermax/Lunima about their own use of Lunima: a problem they hit, a question, or a request.
- failure: 0 or 1 such account in the window. Exactly 2 is recorded as inconclusive and changes no status.
- measurement: the team agent counts distinct authors and commenters of issues, pull requests and (if enabled) discussions in aignermax/Lunima in the window through the read-only GitHub API, minus the excluded set (the maintainer's account, bot and agent accounts, and accounts the maintainer privately marks as colleagues, about 5 min of maintainer time); issues the maintainer filed on someone else's behalf (e.g. #808, #816–#819) do not count and are logged separately as second-hand feedback; star and fork counts and download counts of the last three releases' assets are recorded as context only, never as the verdict, because CI jobs and bots also download assets (see #1635); the public result lists counts and issue numbers only, no account names.
- decision rule:
  - On failure, H0's riskiest assumption has no outside user base to stand on and H0 is set to `weakened`. External validation then has to come from recruited conversations, not from existing users.
  - On success, those issues are the cheapest route to E2 evidence: the maintainer may ask their authors, with approval.
- timebox: 2026-11-23

## Results
Append dated results here. Never edit the pre-registration; new criteria = new experiment.
