"""Inspect actual source IGES parts; requires cadquery-ocp, numpy, matplotlib."""
from pathlib import Path
import collections, json, time
from OCP.IGESControl import IGESControl_Reader
from OCP.BRepBndLib import BRepBndLib
from OCP.Bnd import Bnd_Box
from OCP.TopAbs import TopAbs_FACE, TopAbs_SOLID
from OCP.TopExp import TopExp_Explorer
from OCP.TopoDS import TopoDS
from OCP.BRepAdaptor import BRepAdaptor_Surface
from OCP.GeomAbs import GeomAbs_Cylinder, GeomAbs_SurfaceOfRevolution
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.BRep import BRep_Tool
from OCP.TopLoc import TopLoc_Location

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets' / 'THL300' / 'THL300.igs'
r=IGESControl_Reader()
r.ReadFile(str(SOURCE))
m=r.IGESModel()
solid_entities=[i for i in range(1,m.NbEntities()+1) if m.Entity(i).TypeNumber()==186]
result=[]
for i in solid_entities:
    r.TransferOne(i)
    shape=r.Shape(r.NbShapes())
    bb=Bnd_Box(); BRepBndLib.Add_s(shape,bb)
    exp=TopExp_Explorer(shape,TopAbs_FACE)
    faces=[]
    while exp.More():
        face=TopoDS.Face_s(exp.Current()); fb=Bnd_Box(); BRepBndLib.Add_s(face,fb)
        entity=r.WS().TransferReader().EntityFromShapeResult(face,1)
        surf=BRepAdaptor_Surface(face)
        row={'bounds':fb.Get(),'surface':str(surf.GetType())}
        if entity is not None:
            row['entityType']=entity.TypeNumber()
            source_index=m.Number(entity)
            if source_index>0:
                row['entityIndex']=source_index
            row['color']=entity.Color().RGBIntensity() if entity.Color() else entity.RankColor()
        if surf.GetType()==GeomAbs_Cylinder:
            c=surf.Cylinder(); ax=c.Axis(); row['cylinder']={'radius':c.Radius(),'origin':ax.Location().Coord(),'axis':ax.Direction().Coord()}
        if surf.GetType()==GeomAbs_SurfaceOfRevolution:
            ax=surf.AxeOfRevolution(); row['revolution']={'origin':ax.Location().Coord(),'axis':ax.Direction().Coord()}
        faces.append(row); exp.Next()
    result.append({'entityIndex':i,'bounds':bb.Get(),'faces':faces})
    print('Part',i,'bounds',bb.Get(),'faces',len(faces),'colors',collections.Counter(str(f.get('color')) for f in faces),flush=True)
    print('Major cylinders',[f['cylinder'] for f in faces if 'cylinder' in f and f['cylinder']['radius']>10],flush=True)
    print('Revolution axes',sorted(set(tuple(f['revolution']['origin']) for f in faces if 'revolution' in f)),flush=True)
out=ROOT/'Tools'/'THL300'/'inspection.json'
out.write_text(json.dumps(result,indent=2))
print('Saved',out,flush=True)
