# Experiment pages

`<slug>/page.json` is a draft landing page for a validation experiment. The agent renders
local previews only. Publishing (maintainer): `lunima-team-agent experiment publish <clone>
<slug> --approved-by <you> --contact <address> [--goatcounter <code>]` writes
`docs/x/<slug>/index.html` and stamps the approval; a CI check refuses pages under
`docs/x/` without an approval stamp.
