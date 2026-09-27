"""Independently audit CadSpace exports after the advanced suite."""
import os
from pathlib import Path
import ezdxf

root = Path(os.environ['CADSPACE_EXCHANGE_OUTPUT'])
for name in ('canonical.dxf', 'canonical-binary.dxf'):
    doc = ezdxf.readfile(root / name)
    result = doc.audit()
    assert not result.errors, [(e.code, e.message) for e in result.errors]
    assert not result.fixes, [(e.code, e.message) for e in result.fixes]
    assert 'Sheet A' in doc.layouts.names(), 'Paper-space layout must survive exchange'
    assert len(doc.layouts.get('Sheet A')) == 1
    assert len(doc.modelspace()) == 16
    assert list(doc.modelspace().query('SPLINE')), 'Rational spline lost'
    assert list(doc.modelspace().query('HATCH')), 'Native hatch lost'
    assert list(doc.modelspace().query('MESH')), 'Native indexed mesh lost'
    print(f'PASS ezdxf {ezdxf.__version__}: {name}, zero audit errors or repairs')
