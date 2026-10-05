# Vendored headless port of openEBL's run_verification.py (SiEPIC-Tools layout_check).
#
# Upstream source: https://github.com/SiEPIC/openEBL-2026-10/blob/main/run_verification.py
# Upstream licence: MIT License, Copyright (c) 2025 SiEPIC (see the LICENSE file
# in the openEBL-2026-10 repository).
#
# Same top-cell pick (single top, else the one with most subcells), same EBeam
# technology attach (get_technology_by_name('EBeam') — headless has no GUI
# technology), same SiEPIC.verification.layout_check call writing the .lyrdb next
# to the GDS, same last-stdout-line error count and the same "Unknown error
# occurred" catch-all (find_components raising when no DevRec exists). Adds one
# thing: a per-category error census parsed from the .lyrdb via klayout.rdb, so a
# failing run names the rules instead of just counting them.
#
# Usage: python openebl_verification.py <file.gds>
# Requires: klayout, SiEPIC-Tools (import SiEPIC), siepic_ebeam_pdk.
#
# Lunima's OpenEblSubmissionChecker service and the UnitTests/Export/OpenEbl tests
# both load THIS file — edit it here only, never inline a copy elsewhere.

import os
import sys

gds_file = sys.argv[1]
print('Running SiEPIC-Tools automated verification (Lunima headless port) for file %s' % gds_file)

import klayout.db as pya
import SiEPIC  # noqa: F401
from SiEPIC.verification import layout_check
from SiEPIC.utils import get_technology_by_name
import siepic_ebeam_pdk  # noqa: F401

num_errors = 1
try:
    layout = pya.Layout()
    layout.read(gds_file)
except Exception:
    print('Error loading layout')

try:
    top_cells = layout.top_cells()
    top_cell = layout.top_cell() if len(top_cells) == 1 else max(
        top_cells, key=lambda c: sum(1 for _ in c.each_child_cell()), default=None)
    if not top_cell:
        print('No top cell in the layout')
    else:
        print('Top cell: %s' % top_cell.name)

    # The technology is empty in a headless read; layout_check needs it (the
    # genuine script attaches it the same way — GUI get_technology() is N/A here).
    layout.TECHNOLOGY = get_technology_by_name('EBeam')
    file_lyrdb = os.path.splitext(gds_file)[0] + '.lyrdb'
    num_errors = layout_check(cell=top_cell, verbose=False, file_rdb=file_lyrdb)
except Exception as exc:
    print('Unknown error occurred')
    print('# underlying %s: %s' % (type(exc).__name__, exc))
    num_errors = 1

# Per-category census from the report database (klayout.rdb reads the lyrdb).
# The 'Disconnected pin' and 'Shapes outside component' categories are ALWAYS
# printed (0 when absent or when the run was clean enough to skip writing items),
# so a consumer can assert on the fixed rules instead of relying on a missing line.
counts = {}
if os.path.exists(file_lyrdb):
    try:
        import klayout.rdb as rdb
        db = rdb.ReportDatabase()
        db.load(file_lyrdb)
        for item in db.each_item():
            cat = db.category_by_id(item.category_id()).name()
            counts[cat] = counts.get(cat, 0) + 1
    except Exception as exc:
        print('# category census failed: %s' % exc)
for name in sorted(set(counts) | {'Disconnected pin', 'Shapes outside component'}):
    print('category %s: %d' % (name, counts.get(name, 0)))

print(num_errors)
