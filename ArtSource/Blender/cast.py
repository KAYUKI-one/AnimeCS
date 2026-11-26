"""Blender 4.3 + mmd_tools. blender -b --python cast.py -- SOURCE_ROOT OUTPUT_ROOT [cast name ...]"""
import math
import re
import shutil
import sys
import unicodedata
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).parent))
from motion import author_actions, pose, garment_stances
from drape import mark_surfaces, bake_clearance
from cast_roster import folders as cast_folders, playable

names = {"Rover": "男漂泊", "Cantarella": "坎特", "Lily": "护士服", "Flor": "修模绑骨后）_by_鸣潮_4ce938", "BlackMiku":"黑色礼服miku", "Laevatain":"莱万汀", "Mornye":"莫宁"}
source, destination, *selected = sys.argv[sys.argv.index("--") + 1:]
source, destination = Path(source), Path(destination)
body_only="--body-only" in selected
selected=[name for name in selected if name!="--body-only"]
axes = dict(axis_forward="-Z", axis_up="Y", add_leaf_bones=False, apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_ALL", use_selection=True, path_mode="STRIP",
            mesh_smooth_type="FACE", use_custom_props=False)

def export(path, objects, animated=False):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_mesh_modifiers=not any(o.type=='MESH' and o.data.shape_keys for o in objects), bake_anim=animated, bake_anim_use_all_actions=animated,
                             bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.12, **axes)

def trim(obj, reject):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if reject(f)], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

def flor_color(name):
    name = name.lower()
    groups = [
        ("white eye teeth", "F6EFEB"), ("eyeinner eyes flower", "B73751"),
        ("eyelash eyeliner eyebrow mouth line", "382435"), ("mouth tongue", "BD7481"),
        ("back hair front  hair", "803245"), ("bandage", "E6D8D0"),
        ("metal earrings", "C2A67B"), ("face chin ear body", "F2D1C4"),
        ("clothes up arm clothes", "3C2836"), ("nail", "B64861"), ("pants tacet", "241F2B")]
    for tags, color in groups:
        if any(name.startswith(tag) for tag in re.findall(r"white eye|mouth line|back hair|front  hair|clothes up|arm clothes|\S+", tags)):
            return color
    return "593144"

def materials(label, body, folder):
    slots, discard, mapping, new = list(body.data.materials), set(), {}, []
    records = []
    for idx, mat in enumerate(slots):
        name = mat.name.lower()
        if label=="Laevatain" and name in {"m_actor_laevat_cloth_03","m_eyeshadow_common_01","m_hairshadow_common_01"}:
            discard.add(idx)
            continue
        if ("+" in name or name.startswith(("emo", "heart", "star", "cheek", "><", "mi_star","expressions"))
                or name in {"hi", "eye hi2", "f-eye hi", "f-eye hi2", "eye_hi", "facial","表情","面具影","目影","目hl"}):
            discard.add(idx)
            continue
        node = mat.node_tree.nodes.get("mmd_base_tex") if mat.use_nodes else None
        image = getattr(node, "image", None)
        # PMX texture paths are relative to the package, sometimes one directory out of date.
        if image and not image.size[0]:
            srcname = Path(bpy.path.abspath(image.filepath)).name
            candidates = list(file.parent.parent.rglob(srcname)) if label == "Mornye" else list(file.parent.rglob(srcname))
            if candidates:
                image.filepath = str(candidates[0])
                image.reload()
        if label == "BlackMiku" and mat.name == "头纱" and (not image or not image.size[0]):
            image = bpy.data.images.load(str(file.parent/"data"/"mantilla.png"), check_existing=True)
        if image and image.size[0] > 0:
            key = image.name
            if key not in mapping:
                mat_name = label + "_T%02d" % len(mapping)
                path = folder / (mat_name + ".png")
                src = Path(bpy.path.abspath(image.filepath))
                if src.is_file() and src.suffix.lower() == ".png":
                    shutil.copyfile(src, path)
                else:
                    image.filepath_raw = str(path)
                    image.file_format = "PNG"
                    image.save()
        else:
            rgb = getattr(mat.mmd_material,"diffuse_color",(0.81,0.77,0.75))
            if label=="BlackMiku" and mat.name in ("zip","00011111"): rgb=(.11,.10,.12) if mat.name=="zip" else (.027,.021,.031)
            key = flor_color(mat.name) if label == "Flor" else "".join("%02X" % min(255,round(max(0,c)**(1/2.2)*255)) for c in rgb[:3])
            mat_name = label + "_" + key
        if key not in mapping:
            mapping[key] = len(new)
            new.append(bpy.data.materials.new(mat_name))
        records.append((new[mapping[key]].name, mat.name, image.name if image else "", str(round(mat.mmd_material.alpha,3))))
        slots[idx] = mapping[key]
    (folder / (label+".tsv")).write_text("\n".join("\t".join(r) for r in records),encoding="utf8")
    trim(body, lambda f: f.material_index in discard)
    old = [p.material_index for p in body.data.polygons]
    body.data.materials.clear()
    for m in new:
        body.data.materials.append(m)
    for p, idx in zip(body.data.polygons, old):
        p.material_index = slots[idx]

def panel_bone(name):
    return re.search(r"スカート|skirt|裙|^dress|^belt_[a-e]_|^piaodai",name,re.IGNORECASE) is not None

def smooth(low, high, n):
    t=max(0., min(1., (n-low)/(high-low)))
    return t*t*(3.-2.*t)

def drape_weights(pos, points, weight, result, leg_influence):
    # Keep the waistband on its source joint; distribute loose panels over both legs across the centre seam.
    left, right = (points[s+"UpperLeg"][1] for s in ("Left","Right"))
    knee=(points["LeftLowerLeg"][1].z+points["RightLowerLeg"][1].z)*.5
    hip=(left.z+right.z)*.5
    follow=(1.-smooth(hip-.19,hip+.07,pos.z))*leg_influence
    left_share=smooth(-.10,.10,(pos.x-(left.x+right.x)*.5)*(1 if left.x>right.x else -1))
    shin=(1.-smooth(knee-.14,knee+.10,pos.z))*.96
    for side, share in (("Left",left_share),("Right",1-left_share)):
        for joint, part in (("UpperLeg",1-shin),("LowerLeg",shin)):
            key=side+joint
            result[key]=result.get(key,0.)+weight*follow*share*part
    return weight*(1-follow)

def convert(label, file):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if file.suffix == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(file), load_ui=False, use_scripts=False)
    else:
        bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
        bpy.ops.mmd_tools.import_model(filepath=str(file), types={"MESH","ARMATURE"}, scale=.08, log_level="WARNING")
    mesh = next(o for o in bpy.context.scene.objects if o.type == "MESH" and o.find_armature())
    old = mesh.find_armature()
    verts = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    bottom, top = min(v.z for v in verts), max(v.z for v in verts)
    pelvis = old.data.bones.get("腰") or old.data.bones["下半身"]
    hips = old.matrix_world @ pelvis.head_local
    scale = 1.76 / (top-bottom)
    xform = Matrix.Scale(scale, 4) @ Matrix.Translation((-hips.x, -hips.y, -bottom))
    bone_xform = xform @ old.matrix_world
    points, aliases = {}, {}
    def bone(new, source_name, parent=None):
        orig = old.data.bones.get(source_name)
        if orig is None: return
        points[new] = (parent, bone_xform @ orig.head_local, bone_xform @ orig.tail_local)
        aliases[source_name] = new
    bone("Hips",pelvis.name)
    aliases["下半身"] = "Hips"
    spines = sorted([b for b in old.data.bones if re.fullmatch("上半身[0-9]*",b.name)], key=lambda b:(bone_xform @ b.head_local).z)
    parent = "Hips"
    for new, b in zip(("Spine","Chest","UpperChest"), spines):
        bone(new,b.name,parent)
        parent = new
    bone("Neck","首",parent)
    bone("Head","頭","Neck")
    for side,suffix in (("Left",".L"),("Right",".R")):
        for chain, parent_name in [([("Shoulder","肩"),("UpperArm","腕"),("LowerArm","ひじ"),("Hand","手首")],parent),
                                    ([("UpperLeg","足"),("LowerLeg","ひざ"),("Foot","足首"),("Toes","つま先")],"Hips")]:
            for eng,src in chain:
                bone(side+eng,src+suffix,parent_name)
                parent_name = side+eng
        for local,eng in (("足D","UpperLeg"),("ひざD","LowerLeg"),("足首D","Foot"),("足先EX","Toes"),
                          ("腕捩","UpperArm"),("手捩","LowerArm"),("肩P","Shoulder"),("肩C","Shoulder")):
            aliases[local+suffix] = side+eng
        for finger,jp in [("Thumb","親指"),("Index","人指"),("Middle","中指"),("Ring","薬指"),("Little","小指")]:
            finger_bones = {unicodedata.normalize("NFKC",b.name):b.name for b in old.data.bones if b.name.startswith(jp)}
            prev = side+"Hand"
            for i, joint in enumerate(("Proximal","Intermediate","Distal")):
                key = jp+str(i if finger=="Thumb" else i+1)+suffix
                if key in finger_bones:
                    new = side+finger+joint
                    bone(new, finger_bones[key], prev)
                    prev = new
    def target(name):
        while name:
            if name in aliases: return aliases[name]
            norm = unicodedata.normalize("NFKC",name)
            twist = re.fullmatch(r"(腕捩|手捩)[0-9]+(\.[LR])",norm)
            if twist: return aliases[twist[1]+twist[2]]
            b = old.data.bones.get(name)
            name = b.parent.name if b and b.parent else ""
        return "Hips"
    body = mesh.copy()
    body.data = mesh.data.copy()
    bpy.context.collection.objects.link(body)
    body.shape_key_clear()
    body.animation_data_clear()
    body.modifiers.clear()
    body.constraints.clear()
    body.parent = None
    body.matrix_world = Matrix.Identity(4)
    body.data.transform(xform @ mesh.matrix_world)
    body.name = label + "Body"
    groups = [target(g.name) if g.name in old.data.bones else None for g in body.vertex_groups]
    fabric = [label in garment_stances and g.name in old.data.bones and panel_bone(g.name) for g in body.vertex_groups]
    veil = {v for p in body.data.polygons if label=="BlackMiku" and body.data.materials[p.material_index].name=="头纱" for v in p.vertices}
    if label in garment_stances:
        mark_surfaces(body, points, groups, fabric, veil)
    weights = []
    for v in body.data.vertices:
        w = {}
        for g in v.groups:
            key = groups[g.group]
            if key is None or g.weight <= 0: continue
            amount=drape_weights(v.co,points,g.weight,w,.15 if label=='Cantarella' else 1.) if fabric[g.group] and v.index not in veil else g.weight
            w[key] = w.get(key,0.) + amount
        weights.append(sorted(w.items(), key=lambda t:-t[1])[:4] or [("Hips",1.)])
    body.vertex_groups.clear()
    vg = {name:body.vertex_groups.new(name=name) for name in points}
    for idx,w in enumerate(weights):
        total = sum(a for _,a in w)
        for name,a in w:
            if a>0: vg[name].add([idx],a/total,"REPLACE")
    folder = destination/"Characters"/cast_folders[label]
    folder.mkdir(parents=True, exist_ok=True)
    materials(label, body, folder)
    rig = bpy.data.objects.new("Skeleton",bpy.data.armatures.new("Biped"))
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name,(parent,head,tail) in points.items():
        eb = rig.data.edit_bones.new(name)
        eb.head,eb.tail = head,tail
        if (tail-head).length<.004: eb.tail = head+Vector((0,0,.014))
        if parent: eb.parent = rig.data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    body.parent = rig
    mod = body.modifiers.new("Skin","ARMATURE")
    mod.object = rig
    if label in garment_stances:
        for a in list(bpy.data.actions): bpy.data.actions.remove(a)
        author_actions(rig,label)
        export(folder/(label+"Idle.fbx"),[rig],True)
        for a in list(bpy.data.actions): bpy.data.actions.remove(a)
        rig.animation_data_clear()
        for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
        bpy.context.view_layer.update()
    if body_only and label in garment_stances:
        bake_clearance(label, body, rig)
        export(folder/(label+".fbx"),[rig,body])
        print("DRAPED",label,len(body.data.vertices),flush=True)
        return
    # Only artwork and the normalized deform rig are exported. Bake the sleeve view from these same weights.
    select_group = {g.index for g in body.vertex_groups if any(s in g.name for s in ("Arm","Hand","Thumb","Index","Middle","Ring","Little"))}
    arms = body.copy()
    arms.data = body.data.copy()
    bpy.context.collection.objects.link(arms)
    arm_ids = {v.index for v in arms.data.vertices if sum(g.weight for g in v.groups if g.group in select_group)>.68}
    trim(arms,lambda f:any(v.index not in arm_ids for v in f.verts))
    arms.name=label+"ViewSleeves"
    export(folder/(label+"ViewRig.fbx"),[rig,arms])
    for action,tag in (("View","Arms"),("SidearmView","SidearmArms")):
        pose(rig, action, 0.)
        dg=bpy.context.evaluated_depsgraph_get()
        baked=bpy.data.meshes.new_from_object(arms.evaluated_get(dg),depsgraph=dg)
        fp=bpy.data.objects.new(label+tag+"Mesh",baked)
        if not len(baked.vertices): raise ValueError(label+" has no weighted arm geometry")
        bpy.context.collection.objects.link(fp)
        # Anchor each wrist independently. The source character's shoulder spacing never dictates screen position.
        left_goal=Vector((-.195,-.475,-.245) if action=="SidearmView" else (-.182,-.69,-.205))
        left=left_goal-rig.pose.bones["LeftHand"].head
        right=Vector((-.255,-.428,-.242))-rig.pose.bones["RightHand"].head
        for v,original in zip(baked.vertices,arms.data.vertices):
            strongest=max(original.groups,key=lambda g:g.weight)
            v.co+=left if arms.vertex_groups[strongest.group].name.startswith("Left") else right
        export(folder/(label+tag+".fbx"),[fp])
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
    # One shared Humanoid motion library is retargeted by all four avatars.
    if label=="Rover":
        for a in list(bpy.data.actions): bpy.data.actions.remove(a)
        author_actions(rig)
    if label in garment_stances:
        bake_clearance(label, body, rig)
    export(folder/(label+".fbx"),[rig,body],label=="Rover")
    print("CAST", label, len(body.data.vertices), len(baked.vertices), len(points), flush=True)

for label in selected or playable:
    folders = [d for d in source.iterdir() if d.is_dir() and names[label] in d.name]
    files = [file for folder in folders for file in folder.rglob("*.pmx")]
    file = next(f for f in files if "veil" in f.stem) if label == "BlackMiku" else files[0] if files else next(folders[0].rglob("*.blend"))
    convert(label,file)
