"""Verify packaged EBeam exports with Nazca-only and full SiEPIC Python environments.

Usage: python scripts/verify_gds_export_warnings.py APP NAZCA_ONLY_PYTHON SIEPIC_PYTHON
The bundle and preferences are isolated using the published-app smoke harness.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile

from smoke_published_app import PackagedApp


INSPECT = """
import json, sys
import klayout.db as db
layout = db.Layout()
layout.read(sys.argv[1])
cells = []
for cell in layout.each_cell():
    if 'ebeam_gc_te1550' in cell.name or cell.name.startswith('Waveguide_'):
        cells.append(dict(name=cell.name, shapes=sum(cell.shapes(i).size() for i in layout.layer_indexes()),
                          children=cell.child_cells(), layers=[str(layout.get_info(i))
                          for i in layout.layer_indexes() if not cell.shapes(i).is_empty()]))
print(json.dumps(cells))
"""


def export(app, python, name):
    prefs = Path(app.environment['LOCALAPPDATA']) / 'Lunima' / 'user-preferences.json'
    if os.name != 'nt':
        prefs = Path(app.environment['XDG_DATA_HOME']) / 'Lunima' / 'user-preferences.json'
    prefs.parent.mkdir(parents=True, exist_ok=True)
    prefs.write_text(json.dumps({'CustomPythonPath': str(python)}), encoding='utf-8')
    path = app.sandbox / f'{name}.py'
    result = app.run_json('agent', 'call', 'export_gds', json.dumps({'path': str(path)}),
                          '--design', str(app.example('EBeam Add-Drop Ring.lun')))
    assert result['is_error'] is False, result
    result = result['result']
    assert result['gds_path'] and Path(result['gds_path']).is_file(), result
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('app', type=Path)
    parser.add_argument('nazca_only_python', type=Path)
    parser.add_argument('siepic_python', type=Path)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='lunima-gds-warnings-') as directory:
        app = PackagedApp.copy_into(args.app.resolve(), Path(directory), (), 180)
        reports = []
        for name, python in [('placeholder', args.nazca_only_python), ('real', args.siepic_python)]:
            result = export(app, python.resolve(), name)
            fallback = [warning for warning in result['warnings'] if 'stub box' in warning]
            if name == 'placeholder':
                assert fallback, result
                assert 'exported successfully' not in result['gds_status'], result
            else:
                assert not result['warnings'], result
            inspection = subprocess.run([str(args.siepic_python.resolve()), '-c', INSPECT, result['gds_path']],
                                        capture_output=True, text=True, check=True, timeout=60)
            cells = json.loads(inspection.stdout)
            couplers = [cell for cell in cells if 'ebeam_gc_te1550' in cell['name']]
            assert couplers, cells
            assert all((cell['children'] > 0) == (name == 'real') for cell in couplers), cells
            waveguides = [cell for cell in cells if cell['name'].startswith('Waveguide_')]
            assert waveguides, cells
            assert all(('1/99' in cell['layers']) == (name == 'real')
                       for cell in waveguides), cells
            reports.append(dict(environment=name, bytes=Path(result['gds_path']).stat().st_size,
                                warnings=result['warnings'], status=result['gds_status'], cells=cells))
        print(json.dumps(reports, indent=2))


if __name__ == '__main__':
    main()
