"""Convert the six UV-matched Valve Workshop display meshes. Arguments: unpacked OBJs, Unity art folder."""
import bpy
import sys
from pathlib import Path
from mathutils import Matrix,Vector

sources,output=map(Path,sys.argv[sys.argv.index("--")+1:])
# Grip origins and visual scale are authored in game space, independent of gameplay statistics.
content=[
    ("Street20", "glock-18", .227, .205, .42),
    ("Whitefalcon12s","usp-s",.396,.105,.38),
    ("Verdict57","five-seven",.234,.16,.42),
    ("Dash9","tec-9",.355,.17,.48),
    ("Hummingbird9","mp9",.53,.58,.49),
    ("Spark10","mac-10",.38,.345,.55)]
for name,model,length,grip,grip_height in content:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=str(sources/(model+".obj")),forward_axis="NEGATIVE_Z",up_axis="Y")
    meshes=[o for o in bpy.context.scene.objects if o.type=="MESH"]
    coords=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    rear,front=max(v.y for v in coords),min(v.y for v in coords)
    bottom,top=min(v.z for v in coords),max(v.z for v in coords)
    origin=Vector((0,rear-(rear-front)*grip,bottom+(top-bottom)*grip_height))
    normalize=Matrix.Scale(length/(rear-front),4)@Matrix.Translation(-origin)
    mat=bpy.data.materials.new(name+"_T00")
    for o in meshes:
        o.data.transform(normalize@o.matrix_world)
        o.matrix_world=Matrix.Identity(4);o.data.materials.clear();o.data.materials.append(mat)
        for poly in o.data.polygons: poly.material_index=0
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]
    if len(meshes)>1:bpy.ops.object.join()
    bpy.context.object.name=name
    path=output/name
    path.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=str(path/(name+".fbx")),use_selection=True,axis_forward="-Z",axis_up="Y",
        apply_scale_options="FBX_SCALE_ALL",path_mode="STRIP",bake_anim=False,add_leaf_bones=False)
    print("DISPLAY",name,len(bpy.context.object.data.vertices),flush=True)
