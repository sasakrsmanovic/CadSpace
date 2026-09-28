"""Check actual DXF pattern values and references with an independent implementation."""
import sys
from pathlib import Path
import ezdxf

folder = Path(sys.argv[1])
for name in ('styles.dxf', 'styles-binary.dxf'):
    drawing = ezdxf.readfile(folder / name)
    audit = drawing.audit()
    assert not audit.errors, [(e.code, e.message) for e in audit.errors]
    assert not audit.fixes, [(e.code, e.message) for e in audit.fixes]
    assert drawing.header['$LTSCALE'] == 2.5
    pattern = drawing.linetypes.get('TEST').pattern_tags.tags
    assert [t.value for t in pattern if t.code == 49] == [12, -3, 0, -3]
    assert [t.value for t in pattern if t.code == 40] == [18]
    assert drawing.layers.get('0').dxf.linetype == 'TEST'
    line = drawing.modelspace().query("LWPOLYLINE").first
    assert line.dxftype() == 'LWPOLYLINE' and (line.dxf.flags & 128)
    print(f'PASS ezdxf linetype audit: {name}; zero errors/repairs; native pattern, scale, layer reference and PLINEGEN flag')
