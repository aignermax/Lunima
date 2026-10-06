# Vendored headless port of openEBL's run_submission_checks.py.
#
# Upstream source: https://github.com/SiEPIC/openEBL-2026-10/blob/main/run_submission_checks.py
# Upstream licence: MIT License, Copyright (c) 2025 SiEPIC (see the LICENSE file
# in the openEBL-2026-10 repository).
#
# The port is reduced to klayout.db + siepic_ebeam_pdk so it runs headless without
# the SiEPIC-Tools KLayout plugin environment: same single-top-cell rule, same
# 605 x 410 um floorplan bbox over layers (1,0)+(4,0), same black-box census
# (allow-list, then leftover 998/0 shapes counted from the top cell down after
# clearing the allow-listed cells — the genuine script's replace-with-empty-cell +
# hierarchy walk), same layer-vs-PDK check against the EBeam.lyp layer properties.
# Adds a functional-readiness census (floorplan shapes, opt_in labels) that
# openEBL's run_verification.py (SiEPIC layout_check) requires.
#
# Usage: python openebl_submission_check.py <file.gds>
# Last stdout line is the error count, like the original.
#
# Lunima's OpenEblSubmissionChecker service and the UnitTests/Export/OpenEbl tests
# both load THIS file — edit it here only, never inline a copy elsewhere.

import os, sys
import xml.etree.ElementTree as ET
import klayout.db as pya
import siepic_ebeam_pdk

# Allow-listed black-box cells, verbatim from run_submission_checks.py.
BB_CELLS = [
    'ebeam_gc_te1550', 'ebeam_gc_tm1550',
    'GC_TE_1550_8degOxide_BB', 'GC_TM_1550_8degOxide_BB',
    'ebeam_gc_te1310', 'ebeam_gc_te1310_8deg',
    'GC_TE_1310_8degOxide_BB', 'ebeam_GC_TM_1310_8degOxide',
    'GC_TM_1310_8degOxide_BB', 'GC_TM_1310_8degOxide_BB$1',
    'ebeam_splitter_swg_assist_te1310', 'ebeam_splitter_swg_assist_te1550',
    'ebeam_dream_splitter_1x2_te1550_BB',
]

def pdk_layers():
    lyp = os.path.join(os.path.dirname(siepic_ebeam_pdk.__file__), 'EBeam.lyp')
    layers = set()
    for source in ET.parse(lyp).getroot().iter('source'):
        text = source.text
        if not text:
            continue
        parts = text.split('@')[0].split('/')
        if len(parts) >= 2:
            try:
                layers.add((int(parts[0]), int(parts[1])))
            except ValueError:
                continue
    return layers

def recursive_shapes(layout, top, layer, dt):
    li = layout.find_layer(pya.LayerInfo(layer, dt))
    if li is None:
        return []
    shapes = []
    it = pya.RecursiveShapeIterator(layout, top, li)
    while not it.at_end():
        shapes.append(it.shape())
        it.next()
    return shapes

gds_file = sys.argv[1]
print('Running openEBL submission checks (Lunima headless port) for file %s' % gds_file)
num_errors = 0
layout = pya.Layout()
layout.read(gds_file)

tops = layout.top_cells()
if len(tops) != 1:
    print('Error: layout does not have 1 top cell. It has %s.' % len(tops))
    print(' - cells: %s' % [c.name for c in layout.each_cell()])
    num_errors += 1
    print(num_errors)
    sys.exit(0)
top = tops[0]
print('Top cell: %s' % top.name)

# Floorplan extent: bbox of (1,0)+(4,0) must fit 605 x 410 um (dbu 0.001).
region = pya.Region()
for ld in [(1, 0), (4, 0)]:
    li = layout.find_layer(pya.LayerInfo(*ld))
    if li is not None:
        region += pya.Region(top.bbox_per_layer(li))
region.merge()
if region:
    w = region.bbox().width() * layout.dbu
    h = region.bbox().height() * layout.dbu
    if w > 605.0 or h > 410.0:
        print('Error: Bounding box of selected layers (%.3f um x %.3f um) exceeds allowed size 605.000 um x 410.000 um' % (w, h))
        num_errors += 1
    else:
        print('Bounding box of selected layers is %.3f um x %.3f um' % (w, h))
else:
    print('No shapes found in the specified layers.')
    num_errors += 1

# Black-box census: allowed BB cells, plus leftover 998/0 shapes elsewhere.
# Mirrors the genuine script's semantics: allow-listed BB cells are swapped
# for an EMPTY cell (here: cleared in place), then leftover 998/0 shapes are
# counted walking the hierarchy from the top cell — shapes inside a swapped
# BB cell's own subtree (the PDK's GC cells carry 998/0 TEXT subcells) become
# unreachable and do not count, exactly like SiEPIC's replace_cell +
# cells_containing_bb_layers.
bb_found = sorted({c.name.split('$')[0] for c in layout.each_cell()
                   if c.name.split('$')[0] in BB_CELLS})
print('Performing Black Box cell replacement check')
for name in bb_found:
    print(' - black box cell: %s' % name)
print(' - Number of black box cells to be replaced: %s' % len(bb_found))
for c in layout.each_cell():
    if c.name.split('$')[0] in BB_CELLS:
        c.clear()
unreplaced = []
li998 = layout.find_layer(pya.LayerInfo(998, 0))
if li998 is not None:
    seen = set()
    it = pya.RecursiveShapeIterator(layout, top, li998)
    while not it.at_end():
        seen.add(it.cell().name)
        it.next()
    unreplaced = sorted(seen)
print(' - Number of unreplaced BB cells: %s' % len(unreplaced))
if unreplaced:
    print('ERROR: unidentified black box cells: %s' % unreplaced)
num_errors += len(unreplaced)

# Every design layer must be defined in the EBeam PDK.
pdk = pdk_layers()
for l in layout.layer_infos():
    if (l.layer, l.datatype) not in pdk:
        print('Error: the layer %s/%s in the design is not defined in the PDK.' % (l.layer, l.datatype))
        num_errors += 1

# Functional-readiness census (what run_verification.py / layout_check needs).
floorplans = recursive_shapes(layout, top, 99, 0)
print('Floorplan (99/0) shapes: %s' % len(floorplans))
texts = recursive_shapes(layout, top, 10, 0)
opt_ins = [s.text_string for s in texts
           if s.is_text() and s.text_string.startswith('opt_in')]
print('opt_in labels (10/0): %s' % len(opt_ins))

print(num_errors)
