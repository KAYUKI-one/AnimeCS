"""Measure each iris surface on the original weighted mesh in the same coordinates as cast.py."""
import bpy,sys,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from cast_roster import folders
root,out=map(Path,sys.argv[sys.argv.index("--")+1:])
for label,fragment,iris in [("BlackMiku","礼服miku","Pupils"),("Laevatain","莱万汀","目"),("Mornye","莫宁","Eye_")]:
    package=next(d for d in root.iterdir() if fragment in d.name)
    paths=list(package.rglob("*.pmx"))
    file=next(f for f in paths if "veil" in f.stem) if label=="BlackMiku" else paths[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    bpy.ops.mmd_tools.import_model(filepath=str(file),types={"MESH","ARMATURE"},scale=.08,log_level="ERROR")
    mesh=next(o for o in bpy.context.scene.objects if o.type=="MESH" and o.find_armature())
    world=[mesh.matrix_world@v.co for v in mesh.data.vertices]
    lo=min(v.z for v in world); height=max(v.z for v in world)-lo
    rig=mesh.find_armature()
    hip=rig.matrix_world@(rig.data.bones.get("腰") or rig.data.bones["下半身"]).head_local
    indices={v for p in mesh.data.polygons if mesh.data.materials[p.material_index].name==iris for v in p.vertices}
    if not indices:raise ValueError((label,iris))
    center=[(min(world[i][ax] for i in indices)+max(world[i][ax] for i in indices))*.5 for ax in range(3)]
    scale=1.76/height
    pos=[-(center[0]-hip.x)*scale,(center[2]-lo)*scale,-(center[1]-hip.y)*scale]
    dest=out/'Characters'/folders[label]/(label+'Face.txt')
    dest.write_text(' '.join('%.7f'%v for v in pos),encoding='ascii')
    print('IRIS',label,len(indices),pos,flush=True)

