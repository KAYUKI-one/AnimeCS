"""Author a two-piece reload rig from each existing display mesh, preserving its UVs and material slots.
Run Blender in background with -- <Unity weapons directory>. The display FBXs stay available for inventory and ground props.
"""
from pathlib import Path
import sys
import bmesh
import bpy
from mathutils import Vector

# One vertex identifies each artist-selected UV island in the corresponding source FBX.
islands = {
    "Street20": (668, 746), "Dash9": (4424,), "Hummingbird9": (6386,), "Spark10": (4183, 3784),
    "Heavy50": (41262,), "Watch41S": (0,), "Longglass96": (3787,), "Fold44": (715,),
    "Verdict57": (5818,), "Whitefalcon12s": (2360,), "Feather08": (7809,), "Ember47": ()
}

def connected_labels(mesh, points, ceiling=None):
    parents = list(range(len(points)))
    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]
            i = parents[i]
        return i
    def join(i, j):
        a, b = root(i), root(j)
        if a != b:
            parents[b] = a
    positions = {}
    for i, point in enumerate(points):
        key = tuple(round(x,5) for x in point)
        if key in positions:
            join(i,positions[key])
        else:
            positions[key] = i
    for edge in mesh.edges:
        i,j=edge.vertices
        if ceiling is None or max(points[i].z,points[j].z)<=ceiling+1e-5:
            join(i,j)
    return [root(i) for i in range(len(points))]

directory = Path(sys.argv[sys.argv.index("--")+1])
for name, vertices in islands.items():
    requested=sys.argv[sys.argv.index("--")+2:]
    if requested and name not in requested: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source = directory/name/(name+".fbx")
    bpy.ops.import_scene.fbx(filepath=str(source),use_anim=False)
    body, = [o for o in bpy.context.scene.objects if o.type=="MESH"]
    cut_height=.066
    welded=name=="Ember47"
    if welded:
        bm=bmesh.new(); bm.from_mesh(body.data)
        normal=(body.matrix_world.to_3x3().transposed()@Vector((0,0,1))).normalized()
        point=body.matrix_world.inverted()@Vector((0,0,cut_height))
        bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=1e-7,plane_co=point,plane_no=normal)
        bm.to_mesh(body.data);bm.free();body.data.update()
    points = [body.matrix_world @ v.co for v in body.data.vertices]
    labels = connected_labels(body.data, points, cut_height if welded else None)
    selected = {labels[k] for k in vertices}
    if welded:
        # The bake has an inner shell and several disconnected relief islands on the magazine.
        # Collect all shells in its side-profile area below the socket, clear of the trigger guard.
        selected={labels[i] for i,point in enumerate(points)
                  if point.z<cut_height-.002 and -.27<point.y<-.125}
    faces = set()
    for poly in body.data.polygons:
        take = labels[poly.vertices[0]] in selected and (not welded or (body.matrix_world@poly.center).z<cut_height)
        if take:
            faces.add(poly.index)
    if not faces or len(faces)==len(body.data.polygons):
        raise ValueError("Invalid reload mesh selection for "+name)
    insert = body.copy(); insert.data = body.data.copy()
    bpy.context.collection.objects.link(insert)
    for obj, keep in [(body,False),(insert,True)]:
        bm = bmesh.new(); bm.from_mesh(obj.data)
        selected_faces = [f for f in bm.faces if (f.index in faces) != keep]
        bmesh.ops.delete(bm,geom=selected_faces,context="FACES")
        if welded:
            rim=[v for v in bm.verts if abs((obj.matrix_world@v.co).z-cut_height)<1e-5 and -.26<(obj.matrix_world@v.co).y<-.119]
            bmesh.ops.remove_doubles(bm,verts=rim,dist=.000001)
            edges=[e for e in bm.edges if e.is_boundary and all(abs((obj.matrix_world@v.co).z-cut_height)<1e-5 and -.26<(obj.matrix_world@v.co).y<-.119 for v in e.verts)]
            capped=bmesh.ops.holes_fill(bm,edges=edges,sides=0)["faces"]
            if capped:
                mat=bpy.data.materials.get("ReloadInterior") or bpy.data.materials.new("ReloadInterior")
                mat.diffuse_color=(.045,.052,.055,1)
                obj.data.materials.append(mat)
                for face in capped: face.material_index=len(obj.data.materials)-1
        bm.to_mesh(obj.data); bm.free(); obj.data.update()
    body.name="Body"; insert.name="ReloadInsert"
    parent=bpy.data.objects.new(name+"Handling",None);bpy.context.collection.objects.link(parent)
    body.parent=parent;insert.parent=parent
    bpy.ops.object.select_all(action="DESELECT")
    for obj in (parent,body,insert): obj.select_set(True)
    bpy.context.view_layer.objects.active=parent
    bpy.ops.export_scene.fbx(filepath=str(source.with_name(name+"Handling.fbx")),use_selection=True,
        axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",add_leaf_bones=False,bake_anim=False,path_mode="STRIP")
    print("RIG",name,len(body.data.polygons),len(insert.data.polygons),flush=True)
