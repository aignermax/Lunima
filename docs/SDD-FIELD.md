# Lunima SDD test field

The field exercises three existing journeys on a single versioned acceptance baseline. The contract contains an owner, scope and one observable outcome and fully qualified xUnit method per criterion. The import fixture, headless export session and desktop layer picker exercise different boundaries. The picker proof covers view-model behavior; customer discoverability requires a separate walkthrough. The headless export test injects a warning from its export session.

## From issue to evidence

1. The PO selects a small scope with the feature owner. Edit `docs/superpowers/specs/sdd-field.json` on the pilot branch. Preserve IDs when refining the same requirement. If a criterion is still disputed, record the question on the issue and leave that implementation on hold.
2. Run `python scripts/sdd.py --pin`. Record the returned Git blob ID on a standalone line `SDD-Spec: <blob>` in the tracking issue body. Record who made the decision, its scope and any manual evidence required. The pin identifies the contract; PO acceptance of the change is a separate decision.
3. Start the pilot branch with `sdd/` or label the PR `sdd`. In the PR body include the same pin and a standalone line `SDD-Issue: <number>`. A shared contract changes only after updating the tracking issue. Preserve the references on retries, QA fixes and feedback.
4. Run `python scripts/sdd.py` in the repo root. It runs the selected methods in one bounded, locked xUnit invocation. Inspect `TestResults/sdd/evidence.json` and the TRX. CI checks the issue and PR pins, runs on the PR head, and publishes evidence for that commit. Missing, skipped, failing and stale evidence blocks pilot acceptance.
5. QA inspects the assertions for false positives and records extra evidence required by the issue. PO checks the current head and the current issue pin again at merge time. An issue-only edit does not trigger a PR workflow; update the PR pin or rerun checks. Use the pool's existing review and UX roles for open decisions. Baseline admission is separate from feature approval.

## Pilot coverage

| ID | Boundary | Product example |
|---|---|---|
| IMPORT-01 | Parser and hierarchy importer | The real MZI fixture retains its labelled ports and reconstructs abutting connections. |
| AGENT-01 | Agent API and desktop export workflow | A successful export with placeholder geometry discloses the warning. |
| PICKER-01 | Desktop layer-picker command | Assigning a metal layer to waveguide removes its old role. |

For a negative gate test run `python -m unittest discover -s scripts/tests -p test_sdd.py`. For the exact-pin regression, compare the existing presence assertion against the complete pin set for straight and MMI on the #1650 reproducer. The existing issue owns the defect and budget hold. The labelled-plus-heuristic fixture already encodes the partially labelled case. Agree on its behavior before changing the detector.

For each pilot delivery the PO records time from ready to merge, questions after coding starts, QA rework, effort maintaining the contract and any release escapes. Review these observations after three delivery PRs. Three passing baseline criteria establish the field; they are not three completed feature deliveries.
