"""Restore Source 2 vertex-painted surfaces to a tiled Dust II export.
Run after dust_map.py: python dust_surfaces.py <original world.gltf> <decompiled vtex PNG root> <Unity DustII directory>.
The original position/UV order is checked against the playable mesh before writing the paint buffer.
"""
import argparse
import json
from collections import defaultdict
from pathlib import Path
from urllib.parse import unquote

import numpy as np
from PIL import Image


def mesh_reader(path):
    data = json.loads(path.read_bytes())
    buffers = [(path.parent / unquote(b["uri"])).read_bytes() for b in data["buffers"]]

    def attribute(index):
        a = data["accessors"][index]
        v = data["bufferViews"][a["bufferView"]]
        dtype = np.dtype({5121:"u1",5123:"<u2",5125:"<u4",5126:"<f4"}[a["componentType"]])
        columns = {"SCALAR":1,"VEC2":2,"VEC3":3,"VEC4":4}[a["type"]]
        return np.ndarray((a["count"],columns),dtype=dtype,buffer=buffers[v["buffer"]],
                          offset=v.get("byteOffset",0)+a.get("byteOffset",0),
                          strides=(v.get("byteStride",columns*dtype.itemsize),dtype.itemsize))
    return data, attribute


def recipe(mat):
    v = mat.get("extras",{}).get("vmat",{})
    flags = v.get("IntParams",{})
    if v.get("ShaderName")=="csgo_lightmappedgeneric.vfx" and flags.get("F_LAYERS")==1 and mat.get("alphaMode","OPAQUE")=="OPAQUE":
        return v


def position_key(position):
    return tuple(np.rint(position*10000).astype("i4"))


def collect_paint(world, read):
    groups = defaultdict(list)
    for node in world["nodes"]:
        if "mesh" not in node: continue
        matrix = np.asarray(node.get("matrix",np.eye(4).ravel()),dtype="f4").reshape(4,4).T
        for prim in world["meshes"][node["mesh"]]["primitives"]:
            mat = world["materials"][prim["material"]]
            if not recipe(mat): continue
            a = prim["attributes"]
            used = np.unique(read(prim["indices"]).ravel())
            pos = read(a["POSITION"])[used] @ matrix[:3,:3].T + matrix[:3,3]
            uv = read(a["TEXCOORD_0"])[used]
            tile = np.floor((pos.min(0)+pos.max(0))[[0,2]]/32).astype(int)
            paint = np.rint(read(a["_TEXCOORD_4"])[used,0]*255).astype("u1") if "_TEXCOORD_4" in a else np.zeros(len(pos),"u1")
            groups[("Dust_%d_%d"%tuple(tile),mat["name"],position_key(pos[0]))].append((pos,uv,paint))
    return groups


def add_paint(doc, read, groups, folder):
    # Re-running the preparation only replaces the extension previously appended by this script.
    extra = doc.setdefault("extras",{})
    old = extra.get("paintLayout")
    if old:
        for key,n in zip(("buffers","bufferViews","accessors"),old):
            del doc[key][n:]
    extra["paintLayout"] = [len(doc[k]) for k in ("buffers","bufferViews","accessors")]
    buf = len(doc["buffers"])
    binary = bytearray()
    neutral = {}
    restored = 0
    def accessor(colors):
        start = len(binary)
        binary.extend(colors.tobytes())
        view = len(doc["bufferViews"])
        doc["bufferViews"].append({"buffer":buf,"byteLength":colors.nbytes,"byteOffset":start,"target":34962})
        doc["accessors"].append({"bufferView":view,"componentType":5121,"count":len(colors),"normalized":True,"type":"VEC4"})
        return len(doc["accessors"])-1
    for mesh in doc["meshes"]:
        for prim in mesh["primitives"]:
            attrs = prim["attributes"]
            pos = read(attrs["POSITION"])
            n = len(pos)
            mat = doc["materials"][prim["material"]]
            if not recipe(mat):
                if n not in neutral: neutral[n]=accessor(np.full((n,4),255,"u1"))
                attrs["COLOR_0"]=neutral[n]
                continue
            uv = read(attrs["TEXCOORD_0"])
            colors=np.full((n,4),255,"u1")
            i=0
            while i<n:
                candidates=groups[(mesh["name"],mat["name"],position_key(pos[i]))]
                found=False
                for vertices,texcoords,paint in candidates:
                    end=i+len(vertices)
                    if end<=n and np.allclose(vertices,pos[i:end],rtol=0,atol=.00015) and np.allclose(texcoords,uv[i:end],rtol=0,atol=.0001):
                        colors[i:end,3]=paint
                        i=end
                        found=True
                        break
                if not found: raise ValueError(("Source geometry/UV mismatch",mesh["name"],mat["name"],i,n))
            attrs["COLOR_0"]=accessor(colors)
            restored+=n
    filename="SurfacePaint.bin"
    (folder/filename).write_bytes(binary)
    doc["buffers"].append({"uri":filename,"byteLength":len(binary)})
    return restored


def make_recipes(doc, raw, folder):
    assets=folder/"SurfaceLayers"
    assets.mkdir(exist_ok=True)
    cached={}
    def texture(uri,kind):
        if not uri:return ""
        key=(uri,kind)
        if key in cached:return cached[key]
        src=(raw/uri).with_suffix(".png")
        if not src.exists():raise FileNotFoundError(src)
        name=src.stem
        target=assets/(name+"_"+kind+".png")
        if not target.exists() or target.stat().st_mtime < src.stat().st_mtime:
            with Image.open(src) as im:
                if kind=="surface":
                    # RGB is the tangent normal. Valve stores perceptual roughness in alpha.
                    im=im.convert("RGBA")
                elif kind=="albedo": im=im.convert("RGB")
                else: im=im.convert("RGBA")
                im.thumbnail((2048,2048) if kind=="albedo" else (1024,1024),Image.Resampling.BOX)
                im.save(target,compress_level=4)
        result="SurfaceLayers/"+target.name
        cached[key]=result
        return result
    def uv_matrix(vectors,floats,prefix,enabled):
        if not enabled:return [1,0,0,0,1,0]
        scale=vectors.get(prefix+"Scale",[1,1]);off=vectors.get(prefix+"Offset",[0,0]);center=vectors.get(prefix+"Center",[.5,.5])
        # glTFast stores the Unity mesh UV with Y flipped; apply in Source texture space.
        rad=np.deg2rad(floats.get(prefix+"Rotation",0))
        c,s=np.cos(rad),np.sin(rad)
        a,b,cx,d=c*scale[0],-s*scale[1],s*scale[0],c*scale[1]
        return [a,b,center[0]+off[0]-a*center[0]-b*center[1],cx,d,center[1]+off[1]-cx*center[0]-d*center[1]]
    entries={}
    for mat in doc["materials"]:
        src=recipe(mat)
        if not src or mat["name"] in entries:continue
        t=src["TextureParams"]; f=src.get("FloatParams",{});v=src.get("VectorParams",{}); flags=src["IntParams"]
        def primary(info):
            if not info:return ""
            return unquote(doc["images"][doc["textures"][info["index"]]["source"]]["uri"])
        pbr=mat["pbrMetallicRoughness"]
        enabled=flags.get("F_TEXTURETRANSFORMS",0)
        entries[mat["name"]]=dict(name=mat["name"],baseMap=primary(pbr["baseColorTexture"]),normal=primary(mat.get("normalTexture")),
           orm=primary(pbr.get("metallicRoughnessTexture")),layer=texture(t["g_tLayer2Color"],"albedo"),
           surface=texture(t["g_tLayer2NormalRoughness"],"surface"),mask=texture(t.get("g_tBlendModulation"),"mask"),
           layerTint=v.get("g_vLayer2Tint",[1,1,1,1])[:3],baseTint=v.get("g_vLayer1Tint",[1,1,1,1])[:3],
           blendMode=flags.get("F_FANCY_BLENDING",0),softness=f.get("g_flBlendSoftness",.5),
           colorUV=uv_matrix(v,f,"g_vLayer2TexCoord",enabled),normalUV=uv_matrix(v,f,"g_vLayer2NormalTexCoord",enabled),
           maskUV=uv_matrix(v,f,"g_vBlendModulateTexCoord",enabled))
    singles={}
    for mat in doc["materials"]:
        name=mat["name"]
        vm=mat.get("extras",{}).get("vmat",{})
        pbr=mat.get("pbrMetallicRoughness",{})
        if name in singles or name in entries or mat.get("alphaMode","OPAQUE")!="OPAQUE":continue
        if vm.get("ShaderName") not in ("csgo_vertexlitgeneric.vfx","csgo_lightmappedgeneric.vfx") or not pbr.get("baseColorTexture"):continue
        singles[name]=dict(name=name,baseMap=primary(pbr["baseColorTexture"]),normal=primary(mat.get("normalTexture")),
                 orm=primary(pbr.get("metallicRoughnessTexture")),baseTint=[1,1,1],blendMode=-1)
    doc["extras"]["staticSurfaces"]=list(singles.values())
    doc["extras"]["paintedSurfaces"]=list(entries.values())
    return len(entries),len(cached)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for arg in ("world","raw","folder"):parser.add_argument(arg,type=Path)
    parser.add_argument('--materials-only',action='store_true')
    args=parser.parse_args()
    dest=args.folder/"DustII.gltf"
    if args.materials_only:
        doc=json.loads(dest.read_bytes())
        if not doc.get("extras",{}).get("paintLayout"):raise ValueError("Prepare vertex paint before updating materials")
        count=0
    else:
        world,read=mesh_reader(args.world)
        doc,old=mesh_reader(dest)
        groups=collect_paint(world,read)
        count=add_paint(doc,old,groups,args.folder)
    materials,images=make_recipes(doc,args.raw,args.folder)
    result=json.dumps(doc,separators=(",",":"))
    dest.write_text(result,encoding="utf8")
    print(f"Restored {count:,} vertex samples, {materials} layered surfaces, {images} sampled textures.",flush=True)


if __name__=="__main__": main()
