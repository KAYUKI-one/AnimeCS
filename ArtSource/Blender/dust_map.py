"""Prepare a local Source 2 Viewer Dust II export for Unity glTFast. Arguments: visual glTF, world physics glTF, output."""
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
import json, math, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image
from urllib.parse import unquote

visual, solid, output=map(Path,sys.argv[1:])
output.mkdir(exist_ok=True,parents=True)
def load(path):
    model=json.loads(path.read_bytes());binary=(path.parent/model["buffers"][0]["uri"]).read_bytes()
    def read(index):
        a=model["accessors"][index];v=model["bufferViews"][a["bufferView"]]
        scalar={5121:"u1",5123:"<u2",5125:"<u4",5126:"<f4"}[a["componentType"]]
        dim={"SCALAR":1,"VEC2":2,"VEC3":3,"VEC4":4}[a["type"]]
        dt=np.dtype(scalar)
        return np.ndarray((a["count"],dim),dtype=dt,buffer=binary,offset=v.get("byteOffset",0)+a.get("byteOffset",0),
                          strides=(v.get("byteStride",dim*dt.itemsize),dt.itemsize))
    return model,read
class Writer:
    def __init__(self,name,doc):
        self.name=name;self.file=(output/(name+".bin")).open("wb");self.doc=doc
        self.doc.update(asset={"version":"2.0","generator":"AnimeCS/Dust II"},scene=0,scenes=[{"nodes":[]}],nodes=[],meshes=[],bufferViews=[],accessors=[])
    def array(self,arr,positions=False):
        arr=np.ascontiguousarray(arr);where=self.file.tell()
        self.file.write(arr.tobytes());pad=(-self.file.tell())%4;self.file.write(bytes(pad))
        views=self.doc["bufferViews"]; acc=self.doc["accessors"];a={"bufferView":len(views),"componentType":5125 if arr.dtype.kind=="u" else 5126,
            "count":arr.shape[0],"type":{1:"SCALAR",2:"VEC2",3:"VEC3",4:"VEC4"}[arr.shape[1] if arr.ndim>1 else 1]}
        views.append({"buffer":0,"byteLength":arr.nbytes,"byteOffset":where})
        if positions:a.update(min=arr.min(axis=0).tolist(),max=arr.max(axis=0).tolist())
        acc.append(a);return len(acc)-1
    def mesh(self,name,primitives):
        m=len(self.doc["meshes"]);self.doc["meshes"].append({"name":name,"primitives":primitives})
        self.doc["scenes"][0]["nodes"].append(m);self.doc["nodes"].append({"name":name,"mesh":m})
    def finish(self):
        self.doc["buffers"]=[{"uri":self.name+".bin","byteLength":self.file.tell()}];self.file.close()
        (output/(self.name+".gltf")).write_text(json.dumps(self.doc,separators=(",",":")),encoding="utf8")
model,read=load(visual)
# Deduplicate repeated material variants but retain any differing color, alpha, normals or roughness.
materials=[];by_key={};remap=[]
for mat in model["materials"]:
    key=json.dumps(mat,sort_keys=True);i=by_key.get(key)
    if i is None:i=len(materials);by_key[key]=i;materials.append(mat)
    remap.append(i)
doc={k:model[k] for k in ("textures","images","samplers") if k in model};doc["materials"]=materials
textures=output/"Textures";textures.mkdir(exist_ok=True)
area = defaultdict(float)
tiles=defaultdict(lambda:defaultdict(list))
for node in model["nodes"]:
    if "mesh" not in node:continue
    matrix=np.array(node.get("matrix",np.eye(4).ravel()),dtype="f4").reshape(4,4).T
    for prim in model["meshes"][node["mesh"]]["primitives"]:
        a=prim["attributes"]
        used,ix=np.unique(read(prim["indices"]).ravel(),return_inverse=True);ix=ix.reshape(-1)
        pos=read(a["POSITION"])[used]@matrix[:3,:3].T+matrix[:3,3]
        norm=read(a["NORMAL"])[used]@np.linalg.inv(matrix[:3,:3])
        norm/=np.maximum(.0001,np.linalg.norm(norm,axis=1,keepdims=True))
        uv=read(a["TEXCOORD_0"])[used] if "TEXCOORD_0" in a else np.zeros((len(pos),2),"f4")
        col=tuple(np.floor((pos.min(axis=0)+pos.max(axis=0))[[0,2]]/2/16).astype(int))
        material = remap[prim["material"]]
        tiles[col][material].append((pos,norm,uv,ix))
        tri = pos[ix.reshape(-1, 3)]
        area[material] += np.linalg.norm(np.cross(tri[:,1]-tri[:,0], tri[:,2]-tri[:,0]), axis=1).sum() * .5
# Keep more texels on the largest playable surfaces. Small props and the distant skyline use the original budget.
surface_names = ("plaster","mudbrick","wall","stone","ground","floor","asphalt","door","sand","concrete",
                 "brick","tile","road","tunnel","cobble","pavement","crate","wood")
detail = {}
surface_materials = [i for i in sorted(area, key=area.get, reverse=True) if area[i] >= 70 and
                     "sky" not in materials[i]["name"] and any(k in materials[i]["name"] for k in surface_names)]
for index in surface_materials[:46]:
    material = materials[index]
    pbr = material.get("pbrMetallicRoughness",{})
    for data, limit in [(pbr.get("baseColorTexture"),2048), (material.get("normalTexture"),1024)]:
        if data:
            image = doc["textures"][data["index"]]["source"]
            detail[image] = max(detail.get(image,0),limit)
def shrink(index_item):
    index,item=index_item
    src=visual.parent/unquote(item["uri"]);out=textures/src.name
    with Image.open(src) as im:
        limit=detail.get(index,1024 if "color" in src.name or "diff" in src.name else 512)
        expected=min(max(im.size),limit)
        rewrite=not out.exists() or out.stat().st_mtime<src.stat().st_mtime
        if not rewrite:
            with Image.open(out) as old: rewrite=max(old.size)!=expected
        if rewrite:
            im.thumbnail((limit,limit),Image.Resampling.LANCZOS);im.save(out,compress_level=3)
    item["uri"]="Textures/"+out.name
with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(shrink,enumerate(doc["images"])))
w=Writer("DustII",doc)
for (x,z),batches in sorted(tiles.items()):
    primitives=[]
    for material,items in batches.items():
        positions,normals,uvs,ix=zip(*items)
        offset=0;indices=[]
        for vs,tri in zip(positions,ix):indices.append(tri.astype(np.uint32)+offset);offset+=len(vs)
        attr={"POSITION":w.array(np.concatenate(positions).astype("<f4"),True),"NORMAL":w.array(np.concatenate(normals).astype("<f4")),
              "TEXCOORD_0":w.array(np.concatenate(uvs).astype("<f4"))}
        primitives.append({"attributes":attr,"indices":w.array(np.concatenate(indices).astype("<u4")),"material":material})
    w.mesh("Dust_%d_%d"%(x,z),primitives)
w.finish();del model,tiles,read
physics,read=load(solid);tiles=defaultdict(list)
for node in physics["nodes"]:
    if "mesh" not in node:continue
    mode=node["extras"].get("InteractAs",[])
    if any(m in mode for m in ["csgo_grenadeclip","sky"]):continue
    group="Clip" if mode else "Solid"
    matrix=np.array(node["matrix"],dtype="f4").reshape(4,4).T
    for prim in physics["meshes"][node["mesh"]]["primitives"]:
        p=read(prim["attributes"]["POSITION"])@matrix[:3,:3].T+matrix[:3,3]
        tris=p[read(prim["indices"]).ravel().reshape(-1,3)]
        cent=np.floor(tris.mean(axis=1)[:,[0,2]]/18).astype(int)
        for x,z in np.unique(cent,axis=0):
            keep=np.logical_and(cent[:,0]==x,cent[:,1]==z)
            tiles[(group,x,z)].append(tris[keep])
w=Writer("DustIISolid",{})
for (mode,x,z),parts in sorted(tiles.items()):
    tris=np.concatenate(parts)
    pos,ix=np.unique(tris.reshape(-1,3),axis=0,return_inverse=True)
    prim={"attributes":{"POSITION":w.array(pos.astype("<f4"),True)},"indices":w.array(ix.reshape(-1).astype("<u4"))}
    w.mesh("%s_%d_%d"%(mode,x,z),[prim])
w.finish()
print("Visual materials",len(materials),"collision tiles",len(tiles),flush=True)
