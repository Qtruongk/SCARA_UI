"""Tessellate the supplied THL300 manufacturer IGES, preserving actual geometry.

Run with Python 3.12 and cadquery-ocp==7.8.1.1. Optional --preview requires
numpy and matplotlib. This script never connects to robot hardware.
"""
from __future__ import annotations
from pathlib import Path
import argparse, collections, hashlib, json, math, struct

from OCP.IGESControl import IGESControl_Reader
from OCP.IFSelect import IFSelect_RetDone
from OCP.TopAbs import TopAbs_FACE, TopAbs_REVERSED
from OCP.TopExp import TopExp_Explorer
from OCP.TopoDS import TopoDS
from OCP.BRep import BRep_Tool
from OCP.BRepLib import BRepLib_ToolTriangulatedShape
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.TopLoc import TopLoc_Location

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets' / 'THL300'
SOURCE = ASSETS / 'THL300.igs'
OUTPUT = ASSETS / 'THL300.mesh'
METADATA = ASSETS / 'THL300.metadata.json'
# Entity indices are the four type-186 manifold-solid records in the supplied
# source (IGES directory entry numbers 6011, 6419, 14819, 15091).
PARTS = [(0, 'base', 3006), (1, 'arm1', 3210),
         (2, 'arm2', 7410), (3, 'shaft', 7546)]
STANDARD_COLORS = {
    1:(0,0,0), 2:(255,0,0), 3:(0,255,0), 4:(0,0,255),
    5:(255,255,0), 6:(255,0,255), 7:(0,255,255), 8:(255,255,255)
}
DEFAULT_COLOR = (200, 208, 214)  # IGES rank 0 means unspecified, neutral metal.

def canonical_point(p):
    # IGES is Y-up, fully extended toward -X, shoulder at (-40,*,0).
    # This rigid proper rotation (determinant +1) puts the base mounting plane
    # at Z=0, shoulder at X=0, elbow at X=125 and spindle at X=300.
    return (-p.X()-40.0, p.Z(), p.Y())

def canonical_normal(n, reverse):
    s = -1.0 if reverse else 1.0
    return (-n.X()*s, n.Z()*s, n.Y()*s)

def face_color(face, transfer):
    entity = transfer.EntityFromShapeResult(face, 1)
    if entity is not None:
        col = entity.Color()
        if col is not None:
            return tuple(max(0, min(255, round(c*2.55))) for c in col.RGBIntensity()) + (255,)
        return STANDARD_COLORS.get(entity.RankColor(), DEFAULT_COLOR) + (255,)
    return DEFAULT_COLOR + (255,)

def write_string(f, value):
    data = value.encode('utf-8'); n = len(data)
    while n >= 128:
        f.write(bytes([(n & 127) | 128])); n >>= 7
    f.write(bytes([n])); f.write(data)

def bounds(vertices):
    pts = [v[:3] for v in vertices]
    return [*[min(p[k] for p in pts) for k in range(3)],
            *[max(p[k] for p in pts) for k in range(3)]]

def export(preview=False):
    source_bytes = SOURCE.read_bytes()
    source_hash = hashlib.sha256(source_bytes).digest()
    reader = IGESControl_Reader()
    if reader.ReadFile(str(SOURCE)) != IFSelect_RetDone:
        raise RuntimeError('OpenCascade could not read source IGES')
    model = reader.IGESModel()
    actual_solids = [i for i in range(1,model.NbEntities()+1)
                     if model.Entity(i).TypeNumber()==186]
    if actual_solids != [p[2] for p in PARTS]:
        raise RuntimeError('Unexpected source solids; do not export another CAD with these pivots')
    sections = []; part_metadata = []; discarded_degenerate = 0; corrected_winding = 0
    for group, name, entity_index in PARTS:
        if not reader.TransferOne(entity_index):
            raise RuntimeError(f'Cannot transfer solid {name}')
        shape = reader.Shape(reader.NbShapes())
        mesher = BRepMesh_IncrementalMesh(shape, 0.25, False, math.radians(10), True)
        if not mesher.IsDone():
            raise RuntimeError(f'Meshing failed for {name}')
        by_color = collections.OrderedDict()
        explorer = TopExp_Explorer(shape, TopAbs_FACE)
        face_count = 0
        while explorer.More():
            face = TopoDS.Face_s(explorer.Current()); explorer.Next()
            face_count += 1
            loc = TopLoc_Location()
            tris = BRep_Tool.Triangulation_s(face, loc)
            if tris is None or tris.NbTriangles() == 0:
                raise RuntimeError(f'Face {face_count} in {name} has no triangles')
            BRepLib_ToolTriangulatedShape.ComputeNormals_s(face, tris)
            if not tris.HasNormals():
                tris.ComputeNormals()
            reverse = face.Orientation() == TopAbs_REVERSED
            transform = loc.Transformation()
            rgba = face_color(face, reader.WS().TransferReader())
            section = by_color.setdefault(rgba, {'group':group, 'name':name,
                        'rgba':rgba, 'vertices':[], 'indices':[], 'faces':0})
            section['faces'] += 1
            offset = len(section['vertices'])
            for j in range(1, tris.NbNodes()+1):
                point = tris.Node(j).Transformed(transform)
                normal = tris.Normal(j).Transformed(transform)
                vertex = canonical_point(point)+canonical_normal(normal, reverse)
                # Validate at the precision stored in the runtime resource.
                section['vertices'].append(struct.unpack('<6f',struct.pack('<6f',*vertex)))
            for j in range(1, tris.NbTriangles()+1):
                a,b,c = tris.Triangle(j).Get()
                if reverse: b,c = c,b
                a,b,c = offset+a-1, offset+b-1, offset+c-1
                va,vb,vc = [section['vertices'][k] for k in (a,b,c)]
                ab=[vb[k]-va[k] for k in range(3)]; ac=[vc[k]-va[k] for k in range(3)]
                cross=(ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0])
                if sum(n*n for n in cross) <= 1e-12:
                    discarded_degenerate += 1; continue
                normal=[(va[k+3]+vb[k+3]+vc[k+3])/3 for k in range(3)]
                if sum(cross[k]*normal[k] for k in range(3)) < -1e-10:
                    b,c = c,b; corrected_winding += 1
                section['indices'].extend((a,b,c))
        part_sections = list(by_color.values())
        sections.extend(part_sections)
        all_vertices = [v for s in part_sections for v in s['vertices']]
        part_metadata.append({'groupId':group, 'name':name,
            'sourceEntityIndex':entity_index, 'sourceDirectoryEntry':2*entity_index-1,
            'faceCount':face_count, 'sectionCount':len(part_sections),
            'vertexCount':len(all_vertices),
            'triangleCount':sum(len(s['indices'])//3 for s in part_sections),
            'boundsMm':bounds(all_vertices),
            'colorsRgba':[s['rgba'] for s in part_sections]})
        print(name, json.dumps(part_metadata[-1]), flush=True)
    with OUTPUT.open('wb') as f:
        f.write(b'THL300M1'); f.write(source_hash); f.write(struct.pack('<i',len(sections)))
        for s in sections:
            f.write(struct.pack('<i',s['group'])); write_string(f,s['name'])
            f.write(bytes(s['rgba']))
            f.write(struct.pack('<ii',len(s['vertices']),len(s['indices'])))
            for vertex in s['vertices']:
                f.write(struct.pack('<6f',*vertex))
            f.write(struct.pack(f"<{len(s['indices'])}i",*s['indices']))
    meta = {
        'manufacturer':'Shibaura Machine (Toshiba Machine legacy robot brand)',
        'model':'THL300', 'sourceFile':'THL300.igs', 'sourceBytes':len(source_bytes),
        'sourceSha256':source_hash.hex(), 'sourceCadSystem':'Creo Parametric',
        'sourceCadAssembly':'THL300_3D_ASM', 'sourceCadTimestamp':'2021-11-11T09:37:20',
        'sourceArchive':'THL300.zip supplied by user',
        'meshFile':OUTPUT.name, 'meshSha256':hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
        'format':'THL300M1', 'units':'millimetres',
        'meshTool':'OpenCascade 7.8.1 (cadquery-ocp 7.8.1.1)',
        'chordDeflectionMm':0.25, 'angularDeflectionDeg':10,
        'sectionCount':len(sections), 'parts':part_metadata,
        'triangleCount':sum(p['triangleCount'] for p in part_metadata),
        'vertexCount':sum(p['vertexCount'] for p in part_metadata),
        'discardedDegenerateTriangles':discarded_degenerate,
        'correctedTriangleWinding':corrected_winding,
        'canonicalTransform':{'x':'-sourceX-40','y':'sourceZ','z':'sourceY',
                               'properRotationDeterminant':1},
        'mountingPlaneZMm':0, 'linkLengthsMm':[125,175],
        'shoulderPivotMm':[0,0,162.5], 'elbowPivotMm':[125,0,185],
        'toolPivotMm':[300,0,112], 'referenceToolMm':[300,0,112],
        'referenceJointDegrees':[0,0,0], 'referenceJoint3Mm':160,
        'axisZ':[0,0,1], 'joint3Positive':'up', 'joint3RangeMm':[0,160],
        'shaftTipPhysicalZFormula':'joint3Mm - 48',
        'shaftTranslationZFormula':'joint3Mm - 160',
        'jointHierarchy':{'base':'fixed', 'arm1':'J1', 'arm2':'J1 > J2',
                           'shaft':'J1 > J2 > J3 translation > J4 rotation'},
        'geometryPolicy':'All triangles tessellate the four supplied manifold solids; no proxy robot geometry added.',
        'colorPolicy':'Explicit IGES RGB and standard color ranks preserved; unspecified rank 0 uses neutral metal #C8D0D6.',
        'binaryLayout':'little endian: magic8, sourceSha256[32], int32 sectionCount; per section int32 groupId, .NET 7bit-length UTF8 name, byte RGBA[4], int32 vertexCount, int32 indexCount, vertexCount*(float32 X Y Z NX NY NZ), indexCount*int32 index',
        'manufacturerReference':[
            'https://www.shibaura-machine.co.jp/documents/en/product/robot/download/th/pdf_new/S-2GA23-4-THL300-ENG.pdf',
            'https://www.shibaura-machine.co.jp/en/product/robot/download.html'
        ]
    }
    METADATA.write_text(json.dumps(meta,indent=2)+'\n',encoding='utf-8')
    print('Exported',OUTPUT,OUTPUT.stat().st_size,'bytes',flush=True)
    if preview:
        render_preview(sections)
    return meta

def render_preview(sections):
    import numpy as np
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection
    fig = plt.figure(figsize=(15,7),facecolor='#f7fafc')
    for col,(elev,azim) in enumerate(((18,55),(8,90))):
        ax=fig.add_subplot(1,2,col+1,projection='3d')
        ax.set_facecolor('#f7fafc')
        for s in sections:
            vertices=np.array(s['vertices'])[:,:3]; triangles=np.array(s['indices']).reshape(-1,3)
            polygons=vertices[triangles]
            normals=np.cross(polygons[:,1]-polygons[:,0],polygons[:,2]-polygons[:,0])
            lengths=np.linalg.norm(normals,axis=1); lengths[lengths<1e-12]=1
            normals/=lengths[:,None]
            light=np.array([-0.25,-0.6,0.76]); light/=np.linalg.norm(light)
            intensity=np.maximum(0,np.sum(normals*light,axis=1))*.35+.65
            rgba=np.array(s['rgba'])/255
            facecolors=np.tile(rgba,(len(triangles),1)); facecolors[:,:3]*=intensity[:,None]
            mesh=Poly3DCollection(polygons,facecolors=facecolors,linewidths=0,zsort='average')
            ax.add_collection3d(mesh)
        ax.set_xlim(-200,380); ax.set_ylim(-290,290); ax.set_zlim(0,580)
        ax.set_box_aspect((580,580,580)); ax.view_init(elev=elev,azim=azim)
        ax.set_axis_off()
    fig.tight_layout()
    path=ASSETS/'THL300.preview.png'; fig.savefig(path,dpi=150)
    print('Saved preview',path,flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--preview',action='store_true')
    export(parser.parse_args().preview)
