"""Bake inspection-ready display props from locally exported CS2 glTF skins; keeps UVs and split normals."""
import bpy, json, sys, struct, array, math
from pathlib import Path
from mathutils import Vector
src,art=map(Path,sys.argv[sys.argv.index("--")+1:])
for name,source,shape,grip,length in [('Longglass96','awp',1,.265,1.17),('Feather08','ssg08',1,.275,1.07),('RubyEdge','Knife',0,.25,.37)]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    file=next(f for f in (src/source).glob('*.gltf') if 'physics' not in f.name)
    doc=json.loads(file.read_text()); buf=(file.parent/doc['buffers'][0]['uri']).read_bytes()
    def access(index):
        a=doc['accessors'][index];b=doc['bufferViews'][a['bufferView']]
        n={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4}[a['type']]
        fmt='<'+{5121:'B',5123:'H',5125:'I',5126:'f'}[a['componentType']]*n
        first=b.get('byteOffset',0)+a.get('byteOffset',0); stride=b.get('byteStride',struct.calcsize(fmt))
        return [struct.unpack_from(fmt,buf,first+i*stride) for i in range(a['count'])]
    prim=doc['meshes'][shape]['primitives'][0];v=access(prim['attributes']['POSITION']);uv=access(prim['attributes']['TEXCOORD_0']);nr=access(prim['attributes']['NORMAL'])
    indices=[i[0] for i in access(prim['indices'])];axis=1 if source=='Knife' else 2
    lo=min(q[axis] for q in v); hi=max(q[axis] for q in v);scale=length/(hi-lo)
    h=.158 if source=='awp' else -.003 if source=='ssg08' else 0
    def local(q,point):
        x,y,z=q
        if source=='Knife':return (x,-(y-(lo+(hi-lo)*grip if point else 0)),-z)
        return (x,-(z-(lo+(hi-lo)*grip if point else 0)),y-(h if point else 0))
    # The knife's two source axes are transposed by a proper rotation.
    coords=[tuple(t*scale for t in local(q,True)) for q in v]
    tris=list(zip(indices[::3],indices[1::3],indices[2::3]))
    me=bpy.data.meshes.new(name);me.from_pydata(coords,[],tris);me.update()
    ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob);bpy.context.view_layer.objects.active=ob;ob.select_set(True)
    layer=me.uv_layers.new(name='UVMap')
    for loop in me.loops:
        u,w=uv[loop.vertex_index];layer.data[loop.index].uv=(u,1-w)
    for suffix in (['_T00','_Ruby'] if source=='Knife' else ['_T00']):
        mat=bpy.data.materials.new(name+suffix);mat.use_nodes=True;me.materials.append(mat)
    for f in me.polygons:
        f.use_smooth=True
        if source=='Knife' and max(v[i][1] for i in f.vertices)>.0446:f.material_index=1
    me.normals_split_custom_set_from_vertices([local(q,False) for q in nr])
    folder=art/'Weapons'/name;folder.mkdir(exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(folder/(name+'.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',path_mode='STRIP',bake_anim=False,add_leaf_bones=False)
    # Keep an editable Blender version of the same HD surface and its final textures.
    for m in me.materials:
        tex=folder/(m.name+'.png')
        if not tex.exists():continue
        node=m.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(tex));node.image.pack()
        m.node_tree.links.new(node.outputs['Color'],m.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    if name in ('Feather08','Longglass96','RubyEdge'):
        work=Path(__file__).resolve().parents[1]/'Weapons'/(name+'-展示.blend')
        bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(work))
    print('DISPLAY',name,len(v),len(tris),flush=True)
