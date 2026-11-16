# X-2026-w44-designer-interviews: Do PIC designers actually have to make foreign layouts editable, and how often?
- status: proposed
- method: interview
- autonomy: needs-approval
- assumption: PIC designers who work with layouts made by someone else (a foundry, partner, former colleague or an AI agent) have had to reconstruct components, ports or connectivity from a GDS in the recent past, and it cost them real time.
- hypotheses: H3 (scored); H9 and H2 (recorded only, not scored)
- budget: 0 USD, 3 × 30 min maintainer interviews + 3 × 20 min anonymised write-up; strategist coding ≈ 20k tokens

## Pre-registration
- sample: 3 interviewees who design PIC layouts themselves (a layout engineer in industry, or a PhD student or postdoc in a lab that tapes out). **At least 2 of the 3 must have no affiliation with the maintainer or Akhetonics** to limit friendly bias. Interviewees are not shown Lunima before the questions are answered. A demo afterwards is allowed and recorded separately.
- success: at least 2 of 3 interviewees each describe **one concrete past instance in the last 6 months** of having a GDS without usable ports or netlist that they had to make editable, simulatable or verifiable. Each must estimate that it took **≥ 2 hours** of their own time (their estimate, recorded verbatim).
- failure: **at most 1 of 3** describes such an instance in the last 6 months, **or** every instance described was resolved in under 30 minutes with tools they already have (pin-marker layers, KLayout, re-generating from source, asking the author).
- inconclusive: anything in between, e.g. instances older than 6 months, or time estimates the interviewee cannot give. An inconclusive result does not count as support.
- also recorded (no criterion, evidence only): whether the interviewee has used an AI agent for layout or circuit work in the last 3 months, and how they checked its output (H2/H9); the second workflow they spend most time on that is not GDS import (for the Q1 goal of comparing workflows).
- measurement: the maintainer runs the interviews with the questions in `.discovery/out/actions.json` (interview `foreign-layout-and-verification`). They keep raw notes private and commit one anonymised summary per interview as `discovery/interviews/I-NNNN.md` with consent recorded. The next strategist run codes each summary against the criteria above and quotes the deciding sentence.
- timebox: 2026-12-13

## Results
