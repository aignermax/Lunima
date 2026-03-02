#!/usr/bin/env python3
"""Convert ALL SiEPIC EBeam PDK S-parameter files to Connect-A-PIC PDK JSON."""

import re, json, math, os

SPEED_OF_LIGHT = 299792458.0

def parse_sparam_blocked(path):
    """Parse .sparam blocked format: single quotes, double quotes, or unquoted mode names."""
    blocks = []
    with open(path, 'r') as f:
        lines = f.readlines()
    i = 0
    while i < len(lines):
        line = lines[i].strip()
        # Skip port name headers like ["port 1","LEFT"]
        if line.startswith('['):
            i += 1
            continue
        # Match header with various quote styles
        m = re.match(
            r"""[\(\[]['"]([^'"]+)['"],\s*['"]?(\w[\w ]*\w?)['"]?,\s*\d+,\s*['"]([^'"]+)['"],\s*\d+,\s*['"]?transmission['"]?\s*[\)\]]""",
            line)
        if m:
            out_port, mode, in_port = m.group(1), m.group(2), m.group(3)
            i += 1
            if i >= len(lines):
                break
            shape_m = re.match(r'\((\d+)\s*,\s*3\)', lines[i].strip())
            n = int(shape_m.group(1)) if shape_m else 0
            data = []
            for j in range(n):
                if i + 1 + j >= len(lines):
                    break
                parts = lines[i + 1 + j].strip().split()
                if len(parts) >= 3:
                    data.append((float(parts[0]), float(parts[1]), float(parts[2])))
            blocks.append({'out_port': out_port, 'in_port': in_port, 'mode': mode.strip(), 'data': data})
            i += 1 + n
        else:
            i += 1
    return blocks

def parse_packed_row(path):
    """Parse packed-row format: freq |S11| ang(S11) |S21| ang(S21) |S12| ang(S12) |S22| ang(S22)"""
    blocks = {k: [] for k in ['s11', 's21', 's12', 's22']}
    with open(path, 'r') as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith('[') or line.startswith('('):
                continue
            parts = line.split()
            if len(parts) >= 9:
                freq = float(parts[0])
                blocks['s11'].append((freq, float(parts[1]), float(parts[2])))
                blocks['s21'].append((freq, float(parts[3]), float(parts[4])))
                blocks['s12'].append((freq, float(parts[5]), float(parts[6])))
                blocks['s22'].append((freq, float(parts[7]), float(parts[8])))
    return [
        {'out_port': 'port 1', 'in_port': 'port 1', 'mode': 'TE', 'data': blocks['s11']},
        {'out_port': 'port 2', 'in_port': 'port 1', 'mode': 'TE', 'data': blocks['s21']},
        {'out_port': 'port 1', 'in_port': 'port 2', 'mode': 'TE', 'data': blocks['s12']},
        {'out_port': 'port 2', 'in_port': 'port 2', 'mode': 'TE', 'data': blocks['s22']}
    ]

def freq_to_nm(f):
    return round(SPEED_OF_LIGHT / f * 1e9)

def sample_at_wavelengths(blocks, targets, mode_filter='TE'):
    # Normalize mode filter
    mode_norm = mode_filter.lower().replace(' ', '')
    te = [b for b in blocks if b['mode'].lower().replace(' ', '') == mode_norm]
    if not te:
        te = [b for b in blocks if b['mode'].lower() in ('te', 'mode1', '1')]
    if not te:
        te = blocks  # fallback
    if not te or not te[0]['data']:
        return {}

    all_wl = [freq_to_nm(d[0]) for d in te[0]['data']]
    result = {}
    for t in targets:
        best_i = min(range(len(all_wl)), key=lambda i: abs(all_wl[i] - t))
        conns = []
        for b in te:
            if best_i < len(b['data']):
                _, mag, phase_rad = b['data'][best_i]
                conns.append({
                    'fromPin': b['in_port'],
                    'toPin': b['out_port'],
                    'magnitude': round(mag, 6),
                    'phaseDegrees': round(phase_rad * 180 / math.pi, 2)
                })
        result[all_wl[best_i]] = conns
    return result


targets_1550 = [1500, 1510, 1520, 1530, 1540, 1550, 1560, 1570, 1580, 1590, 1600]
targets_1310 = [1260, 1270, 1280, 1290, 1300, 1310, 1320, 1330, 1340, 1350, 1360]
data_dir = os.path.join(os.path.dirname(__file__), 'sparam-data')

# (name, category, nazcaFunc, nazcaParams, width_um, height_um, pins, file, format, mode_filter)
specs = [
    # === Splitters ===
    ('Y-Branch 1550', 'Splitters', 'ebeam_y_1550', '', 10, 12,
     [('port 1', 0, 6, 180), ('port 2', 10, 3, 0), ('port 3', 10, 9, 0)],
     'y_branch.sparam', 'blocked', 'TE'),

    # === Couplers ===
    ('Directional Coupler TE 1550', 'Couplers', 'ebeam_dc_te1550', 'gap=200e-9', 30, 12,
     [('port 1', 0, 3, 180), ('port 2', 0, 9, 180), ('port 3', 30, 9, 0), ('port 4', 30, 3, 0)],
     'dc_te1550.sparam', 'blocked', 'TE'),

    ('Directional Coupler TE 1550 (Lc=5um)', 'Couplers', 'ebeam_dc_te1550', 'gap=200e-9,Lc=5e-6', 30, 12,
     [('port 1', 0, 3, 180), ('port 2', 0, 9, 180), ('port 3', 30, 9, 0), ('port 4', 30, 3, 0)],
     'dc_te1550_Lc5.sparam', 'blocked', 'TE'),

    ('Broadband DC TE 1550', 'Couplers', 'ebeam_bdc_te1550', '', 60, 20,
     [('port 1', 0, 5, 180), ('port 2', 0, 15, 180), ('port 3', 60, 15, 0), ('port 4', 60, 5, 0)],
     'bdc_te1550.sparam', 'blocked', 'TE'),

    ('DC Halfring-Straight', 'Couplers', 'ebeam_dc_halfring_straight', 'gap=100e-9,radius=3e-6', 20, 10,
     [('port 1', 0, 3, 180), ('port 2', 0, 7, 180), ('port 3', 20, 7, 0), ('port 4', 20, 3, 0)],
     'dc_halfring.dat', 'blocked', 'mode 1'),

    ('Contra-Directional Coupler', 'Filters', 'ebeam_contra_dc', 'N=1000,gap=100e-9', 320, 12,
     [('port 1', 0, 3, 180), ('port 2', 0, 9, 180), ('port 3', 320, 9, 0), ('port 4', 320, 3, 0)],
     'contra_dc.dat', 'blocked', 'TE'),

    # === I/O ===
    ('Grating Coupler TE 1550', 'I/O', 'ebeam_gc_te1550', '', 30, 30,
     [('port 1', 15, 30, 90), ('port 2', 30, 15, 0)],
     'gc_te1550.txt', 'packed', 'TE'),

    ('Grating Coupler TE 1310', 'I/O', 'ebeam_gc_te1310', '', 30, 30,
     [('port 1', 15, 30, 90), ('port 2', 30, 15, 0)],
     'gc_te1310.dat', 'blocked', 'mode 1'),

    ('Taper TE 1550', 'I/O', 'ebeam_taper_te1550', 'w1=0.5e-6,w2=3e-6,length=10e-6', 10, 5,
     [('port 1', 0, 2.5, 180), ('port 2', 10, 2.5, 0)],
     'taper_te1550.dat', 'packed', 'TE'),

    # === Termination ===
    ('Terminator TE 1550', 'Termination', 'ebeam_terminator_te1550', '', 10, 5,
     [('port 1', 0, 2.5, 180)],
     'terminator_te1550.sparam', 'blocked', 'TE'),

    ('Terminator TM 1550', 'Termination', 'ebeam_terminator_tm1550', '', 10, 5,
     [('port 1', 0, 2.5, 180)],
     'terminator_tm1550.sparam', 'blocked', 'TM'),

    ('Disconnected Waveguide TE 1550', 'Termination', 'ebeam_disconnected_te1550', '', 5, 5,
     [('port 1', 0, 2.5, 180)],
     'disconnected_te1550.sparam', 'blocked', 'TE'),
]


def main():
    components = []
    for name, cat, func, params, w, h, pins, src, fmt, mode in specs:
        path = os.path.join(data_dir, src)
        if not os.path.exists(path):
            print(f'SKIP: {src} not found')
            continue

        print(f'Processing {name} ({src})...')

        try:
            if fmt == 'packed':
                blocks = parse_packed_row(path)
            else:
                blocks = parse_sparam_blocked(path)

            if not blocks:
                print(f'  WARNING: No blocks parsed!')
                continue

            print(f'  {len(blocks)} blocks, mode filter={mode}')

            t = targets_1310 if '1310' in name else targets_1550
            sampled = sample_at_wavelengths(blocks, t, mode)
            print(f'  {len(sampled)} wavelengths sampled')

            if not sampled:
                print(f'  WARNING: No wavelengths sampled!')
                continue

            wl_data = [{'wavelengthNm': wl, 'connections': conns}
                       for wl, conns in sorted(sampled.items())]

            center = 1310 if '1310' in name else 1550
            conn_center = min(sampled.items(),
                              key=lambda x: abs(x[0] - center))[1] if sampled else []

            components.append({
                'name': name, 'category': cat,
                'nazcaFunction': func, 'nazcaParameters': params,
                'widthMicrometers': w, 'heightMicrometers': h,
                'pins': [{'name': p[0], 'offsetXMicrometers': p[1],
                           'offsetYMicrometers': p[2], 'angleDegrees': p[3]}
                         for p in pins],
                'sMatrix': {
                    'wavelengthNm': center,
                    'connections': conn_center,
                    'wavelengthData': wl_data
                }
            })
            print(f'  OK: {len(conn_center)} connections at ~{center}nm')
        except Exception as e:
            print(f'  ERROR: {e}')
            import traceback
            traceback.print_exc()

    pdk = {
        'fileFormatVersion': 1,
        'name': 'SiEPIC EBeam PDK',
        'description': 'UBC SiEPIC EBeam PDK (SOI 220nm) - S-parameters from Lumerical simulations',
        'foundry': 'UBC / SiEPIC',
        'version': '0.5.4',
        'defaultWavelengthNm': 1550,
        'nazcaModuleName': 'siepic_ebeam_pdk',
        'components': components
    }

    out = os.path.join(os.path.dirname(__file__), '..', 'CAP-DataAccess', 'PDKs', 'siepic-ebeam-pdk.json')
    with open(out, 'w') as f:
        json.dump(pdk, f, indent=2)
    print(f'\nWrote {out}')
    print(f'Total components: {len(components)}')
    for c in components:
        wl = len(c['sMatrix'].get('wavelengthData', []))
        cn = len(c['sMatrix'].get('connections', []))
        print(f'  {c["name"]:45s} {c["category"]:15s} {len(c["pins"])}p  {wl}wl  {cn}conn')


if __name__ == '__main__':
    main()
