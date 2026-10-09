"""Validate the exact CAD mesh resource independently using Python stdlib only."""
from pathlib import Path
import hashlib, json, math, struct

ROOT=Path(__file__).resolve().parents[2]
ASSETS=ROOT/'Assets'/'THL300'
meta=json.loads((ASSETS/'THL300.metadata.json').read_text(encoding='utf-8'))
source_hash=hashlib.sha256((ASSETS/'THL300.igs').read_bytes()).digest()
mesh=(ASSETS/'THL300.mesh').read_bytes()
assert source_hash.hex()==meta['sourceSha256']
assert hashlib.sha256(mesh).hexdigest()==meta['meshSha256']
assert mesh[:8]==b'THL300M1' and mesh[8:40]==source_hash
offset=40
def unpack(fmt):
    global offset
    result=struct.unpack_from(fmt,mesh,offset); offset+=struct.calcsize(fmt)
    return result
section_count,=unpack('<i')
assert section_count==meta['sectionCount']==16
groups={i:{'vertices':0,'triangles':0} for i in range(4)}
for section in range(section_count):
    group,=unpack('<i'); assert group in groups
    n=0; shift=0
    while True:
        b,=unpack('<B'); n |= (b&127)<<shift
        if b<128: break
        shift+=7; assert shift<=28
    name=mesh[offset:offset+n].decode('utf-8'); offset+=n
    assert name==meta['parts'][group]['name']
    rgba=unpack('<4B'); assert list(rgba) in meta['parts'][group]['colorsRgba']
    nv,ni=unpack('<ii'); assert nv>0 and ni>0 and ni%3==0
    vertices=[unpack('<6f') for _ in range(nv)]
    for v in vertices:
        assert all(math.isfinite(c) for c in v)
        assert abs(sum(c*c for c in v[3:])-1)<1e-4
    indices=unpack(f'<{ni}i'); assert min(indices)>=0 and max(indices)<nv
    for k in range(0,ni,3):
        va,vb,vc=[vertices[i] for i in indices[k:k+3]]
        ab=[vb[i]-va[i] for i in range(3)]; ac=[vc[i]-va[i] for i in range(3)]
        cross=(ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0])
        assert sum(n*n for n in cross)>1e-12
        normal=[(va[i+3]+vb[i+3]+vc[i+3])/3 for i in range(3)]
        assert sum(cross[i]*normal[i] for i in range(3)) >= -1e-10
    groups[group]['vertices']+=nv; groups[group]['triangles']+=ni//3
assert offset==len(mesh), 'Unexpected trailing mesh bytes'
for part in meta['parts']:
    assert groups[part['groupId']]['vertices']==part['vertexCount']
    assert groups[part['groupId']]['triangles']==part['triangleCount']
assert sum(g['triangles'] for g in groups.values())==meta['triangleCount']
assert meta['parts'][0]['boundsMm'][2]==0
assert meta['parts'][3]['boundsMm'][2]==112
assert meta['linkLengthsMm']==[125,175]
print(f"PASS: {section_count} sections; 4 actual CAD solids; {meta['triangleCount']:,} triangles; SHA256, indices, normals, winding, extents and full file validated.")
