"""Independent verification of native width groups, not just a CadSpace self-roundtrip."""
import os
from pathlib import Path
import ezdxf
folder = Path(os.environ['CADSPACE_EXCHANGE_OUTPUT'])
for name in ('widths.dxf', 'widths-binary.dxf'):
    drawing = ezdxf.readfile(folder / name)
    audit = drawing.audit()
    assert not audit.errors, [(e.code, e.message) for e in audit.errors]
    assert not audit.fixes, [(e.code, e.message) for e in audit.fixes]
    entities = list(drawing.modelspace())
    assert len(entities) == 5 and all(e.dxftype() == 'LWPOLYLINE' for e in entities)
    assert entities[0].dxf.const_width == 8
    assert entities[1].get_points('se') == [(2,12), (0,0)]
    assert entities[2].get_points('seb') == [(6,10,1), (4,8,0)]
    assert abs(entities[3].dxf.const_width - 6) < 1e-10
    assert entities[3].dxf.extrusion.isclose((1/14**.5, 2/14**.5, 3/14**.5))
    assert entities[4].closed and entities[4].get_points('se')[-1] == (6,7)
    print(f'PASS ezdxf width audit: {name}; zero errors/repairs; native constant/tapered/curved/OCS/closing-segment widths')
