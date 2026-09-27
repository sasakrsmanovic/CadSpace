from pathlib import Path
import ezdxf
from ezdxf.math import Vec3

assert ezdxf.__version__ == '1.4.4', 'Fixture generation requires ezdxf==1.4.4'
root=Path(__file__).resolve().parent; root.mkdir(parents=True,exist_ok=True)
doc=ezdxf.new('R2013'); doc.units=4; m=doc.modelspace()
m.add_circle((10,20,3),5,dxfattribs={'extrusion':(1,2,3)})
m.add_arc((20,20,0),8,30,250,dxfattribs={'extrusion':(0,1,0)})
m.add_polyline3d([(0,0,0),(5,2,7),(10,3,-2)],close=True)
m.add_rational_spline([(1,0,0),(1,1,0),(0,1,0)],[1,2**-.5,1],degree=2)
m.add_solid([(0,0,0),(10,0,0),(0,10,0),(10,10,0)])
m.add_3dface([(0,0,5),(10,0,5),(10,10,8),(0,10,6)])
mesh=m.add_mesh()
with mesh.edit_data() as d:
    d.vertices=[(0,0,20),(10,0,20),(10,10,20),(0,10,20),(5,5,35)]
    d.faces=[(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)]
polyface=m.add_polyface(); polyface.append_faces([[(30,0,0),(40,0,0),(40,10,0),(30,10,0)]])
h=m.add_hatch(color=3); h.paths.add_polyline_path([(0,0),(10,0),(10,10),(0,10)],is_closed=True,flags=1);h.paths.add_polyline_path([(2,2),(8,2),(8,8),(2,8)],is_closed=True,flags=0)
h=m.add_hatch(color=4);h.set_pattern_fill('TEST',pattern_type=0,definition=[[0,(0,0),(0,2),[3,-1]]]);h.paths.add_polyline_path([(20,0),(30,0),(30,10),(20,10)],is_closed=True)
h=m.add_hatch(color=2);path=h.paths.add_edge_path();path.add_arc((50,0),5,0,360,ccw=True)
n=Vec3(1,2,3).normalize(); major=n.cross(Vec3(0,0,1)).normalize()*10
m.add_ellipse((0,50,5),major,0.4,dxfattribs={'extrusion':n})
b=doc.blocks.new('PART',base_point=(2,3,0));b.add_line((2,3,0),(12,3,0));b.add_attdef('LABEL',insert=(2,3,0),height=2)
insert=m.add_blockref('PART',(100,20,0),dxfattribs={'xscale':2,'yscale':3,'rotation':30})
insert.add_attrib('LABEL','Part A',insert=(100,20,0),dxfattribs={'height':2})
m.add_text('Oblique',dxfattribs={'insert':(50,50,0),'height':4,'width':1.5,'oblique':20,'rotation':30})
m.add_line((999,999,999),(1000,1000,1000),dxfattribs={'invisible':1})
layout=doc.layouts.new('Sheet A');layout.add_line((10000,0,0),(20000,0,0))
doc.saveas(root/'independent-r2013.dxf');doc.saveas(root/'independent-r2013-binary.dxf',fmt='bin')
# A real byte-code R12 fixture, not text mislabeled binary.
r12=ezdxf.new('R12');r12.modelspace().add_line((1,2,3),(4,5,6));r12.saveas(root/'independent-r12-binary.dxf',fmt='bin')

import zipfile
with zipfile.ZipFile(root / 'independent.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for source in sorted(root.glob('independent-*.dxf')):
        archive.write(source, source.name)
print('Generated fixtures with ezdxf', ezdxf.__version__)
