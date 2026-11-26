"""Fit the demo's embroidered team insignia to the native surface and skin weights of each cast member."""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

art = Path(sys.argv[sys.argv.index("--") + 1])
folder = art / "Wearables" / "TeamMarks"
folder.mkdir(parents=True, exist_ok=True)


def fit_character(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(art / "Characters" / name / (name + ".fbx")),
                            use_anim=False, ignore_leaf_bones=False)
    body = max((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: len(o.data.vertices))
    rig = body.find_armature()
    points = [body.matrix_world @ v.co for v in body.data.vertices]
    weights = [{body.vertex_groups[w.group].name: w.weight for w in v.groups if w.weight > .0001}
               for v in body.data.vertices]
    body.data.calc_loop_triangles()
    triangles = [tuple(t.vertices) for t in body.data.loop_triangles]
    verts, uv, influences, faces = [], [], [], []

    def bone(n):
        return rig.matrix_world @ rig.data.bones[n].head_local

    def region(names):
        scores = [sum(row.get(n, 0) for n in names) for row in weights]
        ids = [tri for tri in triangles if sum(scores[i] for i in tri) > 1.4]
        return BVHTree.FromPolygons(points, ids, all_triangles=True), ids

    def projected(surface, center, normal):
        tree, faces = surface
        hit, _, index, _ = tree.ray_cast(center + normal * .24, -normal, .48)
        if hit is None:
            hit, _, index, _ = tree.find_nearest(center)
        a, b, c = faces[index]
        ab, ac, ap = points[b] - points[a], points[c] - points[a], hit - points[a]
        denom = ab.dot(ab) * ac.dot(ac) - ab.dot(ac) ** 2
        wb = (ac.dot(ac) * ap.dot(ab) - ab.dot(ac) * ap.dot(ac)) / denom if denom > 1e-16 else 0
        wc = (ab.dot(ab) * ap.dot(ac) - ab.dot(ac) * ap.dot(ab)) / denom if denom > 1e-16 else 0
        row = {}
        for corner, factor in ((a, 1-wb-wc), (b, wb), (c, wc)):
            for group, weight in weights[corner].items():
                row[group] = row.get(group, 0) + max(0, factor) * weight
        row = sorted(row.items(), key=lambda p: -p[1])[:4]
        total = sum(weight for _, weight in row)
        return hit + normal * .0035, [(group, weight / total) for group, weight in row]

    def patch(surface, generate):
        start = len(verts)
        cols, rows = 12, 10
        for j in range(rows + 1):
            v = j / rows
            width = min(1, .57 + 1.75*v, .85 + (1-v)*1.5)
            for i in range(cols + 1):
                u = i / cols
                center, normal = generate((u*2-1)*width, v)
                co, row = projected(surface, center, normal)
                verts.append(co)
                influences.append(row)
                uv.append((u, v))
        for j in range(rows):
            for i in range(cols):
                a = start + j*(cols+1)+i
                faces.append((a, a+1, a+cols+2, a+cols+1))

    chest = bone("UpperChest")
    torso = region(("Spine", "Chest", "UpperChest", "LeftShoulder", "RightShoulder"))
    for side, horizontal, width, height in ((-1,.071,.043,.078), (1,0,.070,.093)):
        anchor = chest + Vector((horizontal, 0, -.012 if side == 1 else -.05))
        axis = Vector((0, side, 0))
        patch(torso, lambda u,v: (anchor+Vector((u*width,0,(v-.5)*height)),axis))
    for arm in ("Left", "Right"):
        start, end = bone(arm + "UpperArm"), bone(arm + "LowerArm")
        arm_axis = (end-start).normalized()
        front = Vector((0,-1,0))
        front = (front - arm_axis*arm_axis.dot(front)).normalized()
        top = arm_axis.cross(front).normalized()
        if top.z < 0:
            top.negate()
        surface = region((arm + "UpperArm", arm + "Shoulder"))
        def sleeve(u,v):
            angle = math.radians(18+u*49)
            normal = top*math.cos(angle) + front*math.sin(angle)
            center = start.lerp(end, .32) + arm_axis*((.5-v)*.082)
            return center, normal
        patch(surface, sleeve)
    mesh = bpy.data.meshes.new(name + " embroidered patches")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    texture_uv = mesh.uv_layers.new(name="Embroidery")
    for poly in mesh.polygons:
        poly.use_smooth = True
        for loop in poly.loop_indices:
            texture_uv.data[loop].uv = uv[mesh.loops[loop].vertex_index]
    item = bpy.data.objects.new("Shoulder and vest insignia", mesh)
    bpy.context.collection.objects.link(item)
    item.data.materials.append(bpy.data.materials.new("Team embroidery"))
    groups = {}
    for index,row in enumerate(influences):
        for group,weight in row:
            if group not in groups:
                groups[group] = item.vertex_groups.new(name=group)
            groups[group].add([index], weight, "REPLACE")
    modifier = item.modifiers.new("Cloth attachment", "ARMATURE")
    modifier.object = rig
    item.parent = rig
    item.matrix_parent_inverse = rig.matrix_world.inverted()
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    item.select_set(True)
    bpy.context.view_layer.objects.active = item
    bpy.ops.export_scene.fbx(filepath=str(folder / (name+"TeamPatch.fbx")),use_selection=True,
                             axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",
                             bake_anim=False,add_leaf_bones=False,path_mode="STRIP")
    print("INSIGNIA", name, len(faces), flush=True)


for character in ("Rover", "Flor", "Lily", "Endmin", "Cantarella"):
    fit_character(character)
