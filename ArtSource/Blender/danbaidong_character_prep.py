"""Portable DanbaidongRP Character Mesh & Map Preprocessor for Blender 4.x / 5.x.

Prepares any anime character FBX (from MMD/PMX, VRoid, or custom rigs) for DanbaidongRP PBRToon:
1. Optional non-destructive material sub-region splitting (e.g. Eyelash/Eyebrow, Skin, Stockings)
   without altering vertex count, vertex order, bone weights, or shape keys.
2. Symmetric frontal Face SDF UV projection into UV1 (TEXCOORD1) + UV2 (TEXCOORD2), plus a
   512x512 sculpted anime Face SDF texture (R: directional shadow SDF, G/B: nose spec, A: mask).
3. Blended spherical normals on Hair surfaces to eliminate faceted/plastic shading while keeping
   strand silhouettes, plus cylindrical/azimuthal Hair Specular UVs in UV1 for PBRToon/Hair.
4. Angle-weighted tangent-space smooth normals baked into Vertex Color RGB (linear) and per-role
   outline width masks baked into Vertex Color Alpha (DanbaidongRP SN_VertColor standard).
5. First-person/head anchor calibration (<Actor>Face.txt) when not already present.

Usage:
    blender -b --python danbaidong_character_prep.py -- INPUT.fbx OUTPUT.fbx [--actor Lily] [--profile lily] [--out-dir FOLDER]
"""

import argparse
import math
import sys
from collections import defaultdict
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector


# Built-in role detection heuristics (portable across MMD / VRoid / anime characters)
def default_role(mat_name: str) -> str:
    n = mat_name.lower()
    if any(k in n for k in ("eye", "iris", "pupil", "目")):
        return "eye"
    if any(k in n for k in ("lash", "brow", "eyeline", "眉", "睫")):
        return "lash"
    if any(k in n for k in ("face", "head", "颜", "顔", "脸")):
        return "face"
    if any(k in n for k in ("hair", "bang", "fringe", "发", "髪")):
        return "hair"
    if any(k in n for k in ("stocking", "tight", "sock", "丝袜", "袜")):
        return "stockings"
    if any(k in n for k in ("skin", "body", "肌", "肤")):
        return "skin"
    if any(k in n for k in ("veil", "lace", "trans", "alpha", "glow", "纱")):
        return "transparent"
    if any(k in n for k in ("metal", "gold", "silver", "armor", "金属")):
        return "metal"
    return "cloth"


PROFILES = {
    "lily": {
        "roles": {
            "Lily_T00": "eye",
            "Lily_T01": "face",
            "Lily_Lash": "lash",
            "Lily_T02": "metal",
            "Lily_Skin": "skin",
            "Lily_Stockings": "stockings",
            "Lily_T03": "cloth",
            "Lily_T04": "cloth",
            "Lily_T05": "hair",
            "Lily_T06": "hair",
            "Lily_T07": "transparent",
        },
        "splits": [
            # Split eyelashes & eyebrows out of Lily_T01 (face atlas) for FLAG_EYELASH / EyelashOutter see-through
            {
                "source": "Lily_T01",
                "target": "Lily_Lash",
                "rule": "lily_lash",
            },
            # Split hands/arms/neck skin out of Lily_T03 for warm matte SkinRamp shading
            {
                "source": "Lily_T03",
                "target": "Lily_Skin",
                "rule": "lily_skin",
            },
            # Split sheer lavender leg stockings out of Lily_T03 for PBRToon/Stockings shader
            {
                "source": "Lily_T03",
                "target": "Lily_Stockings",
                "rule": "lily_stockings",
            },
        ],
        "hair_sphere_blend": 0.62,
        "face_normal_blend": 0.35,
    },
}


def find_islands(bm, mat_index: int):
    faces = [f for f in bm.faces if f.material_index == mat_index]
    visited = set()
    islands = []
    for f in faces:
        if f.index in visited:
            continue
        comp = []
        stack = [f]
        visited.add(f.index)
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.material_index == mat_index and nf.index not in visited:
                        visited.add(nf.index)
                        stack.append(nf)
        islands.append(comp)
    return islands


def match_split_rule(rule: str, comp, uv_layer) -> bool:
    us = [l[uv_layer].uv.x for f in comp for l in f.loops]
    vs = [l[uv_layer].uv.y for f in comp for l in f.loops]
    xs = [v.co.x for f in comp for v in f.verts]
    ys = [v.co.y for f in comp for v in f.verts]
    zs = [v.co.z for f in comp for v in f.verts]
    u_avg = sum(us) / len(us)
    v_avg = sum(vs) / len(vs)
    x_abs_max = max(abs(x) for x in xs)
    y_max = max(ys)
    z_min, z_max = min(zs), max(zs)

    if rule == "lily_lash":
        # Separate eyelash/eyebrow cards on front of eyes/brows (exclude main face, ears, mouth)
        if len(comp) > 1500:
            return False
        if z_min >= 1.565 and z_max <= 1.635 and y_max < -0.045 and x_abs_max > 0.015:
            # Exclude main skin area; eyelashes/brows sit in lower dark band v <= 0.33 or edge strips
            if v_avg <= 0.33 or u_avg <= 0.05 or u_avg >= 0.95:
                return True
        return False

    if rule == "lily_skin":
        # Hands, fingers, nails, arm skin in top-left skin patch of Lily_T03.png
        return u_avg <= 0.36 and v_avg >= 0.74

    if rule == "lily_stockings":
        # Left & right sheer lavender leg stockings in middle-left of Lily_T03.png
        return u_avg <= 0.36 and 0.12 <= v_avg < 0.74 and z_min >= 0.20 and z_max <= 0.95

    return False


def ensure_material(mesh_data, name: str) -> int:
    for idx, m in enumerate(mesh_data.materials):
        if m and m.name == name:
            return idx
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mesh_data.materials.append(mat)
    return len(mesh_data.materials) - 1


def apply_material_splits(obj, splits):
    if not splits:
        return
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    uv_layer = bm.loops.layers.uv[0]

    summary = defaultdict(int)
    for spec in splits:
        src_name = spec["source"]
        dst_name = spec["target"]
        rule = spec["rule"]
        src_idx = next((i for i, m in enumerate(me.materials) if m and m.name == src_name), -1)
        if src_idx < 0:
            continue
        dst_idx = ensure_material(me, dst_name)
        islands = find_islands(bm, src_idx)
        for comp in islands:
            if match_split_rule(rule, comp, uv_layer):
                for f in comp:
                    f.material_index = dst_idx
                summary[dst_name] += len(comp)

    bm.to_mesh(me)
    bm.free()
    me.update()
    for name, count in summary.items():
        print(f"  Split -> {name}: {count} polygons", flush=True)


def get_role_map(me, profile_cfg):
    overrides = profile_cfg.get("roles", {}) if profile_cfg else {}
    role_by_slot = {}
    for idx, m in enumerate(me.materials):
        name = m.name if m else ""
        role_by_slot[idx] = overrides.get(name, default_role(name))
    return role_by_slot


def compute_face_and_head_bounds(me, role_by_slot):
    face_verts = set()
    eye_verts = set()
    hair_verts = set()
    for p in me.polygons:
        r = role_by_slot.get(p.material_index, "cloth")
        if r == "face":
            face_verts.update(p.vertices)
        elif r == "eye":
            eye_verts.update(p.vertices)
        elif r == "hair":
            hair_verts.update(p.vertices)

    if not face_verts:
        raise ValueError("No face polygons found for Face SDF projection.")

    fcoords = [me.vertices[i].co for i in face_verts]
    f_x_max = max(abs(c.x) for c in fcoords)
    f_z_min = min(c.z for c in fcoords)
    f_z_max = max(c.z for c in fcoords)
    f_y_min = min(c.y for c in fcoords)
    f_y_max = max(c.y for c in fcoords)

    if eye_verts:
        ecoords = [me.vertices[i].co for i in eye_verts]
        eye_center = Vector((
            0.5 * (min(c.x for c in ecoords) + max(c.x for c in ecoords)),
            0.5 * (min(c.y for c in ecoords) + max(c.y for c in ecoords)),
            0.5 * (min(c.z for c in ecoords) + max(c.z for c in ecoords)),
        ))
    else:
        eye_center = Vector((0.0, f_y_min + 0.02, f_z_min + 0.56 * (f_z_max - f_z_min)))

    head_center = Vector((
        0.0,
        0.5 * (f_y_min + f_y_max) + 0.025,
        f_z_min + 0.55 * (f_z_max - f_z_min),
    ))
    return {
        "face_width": f_x_max * 2.10,
        "face_chin": f_z_min,
        "face_height": (f_z_max - f_z_min) * 1.08,
        "eye_center": eye_center,
        "head_center": head_center,
        "hair_z_min": min((me.vertices[i].co.z for i in hair_verts), default=f_z_min),
        "hair_z_max": max((me.vertices[i].co.z for i in hair_verts), default=f_z_max + 0.08),
    }


def write_uvs_for_danbaidong(me, role_by_slot, bounds):
    # Ensure at least 3 UV channels: UV0 (diffuse), UV1 (Face SDF / Hair Spec), UV2 (backup Face SDF)
    while len(me.uv_layers) < 3:
        me.uv_layers.new(name=f"UV{len(me.uv_layers)}")

    uv0 = me.uv_layers[0].data
    uv1 = me.uv_layers[1].data
    uv2 = me.uv_layers[2].data

    width = max(bounds["face_width"], 1e-4)
    chin = bounds["face_chin"]
    height = max(bounds["face_height"], 1e-4)
    head_c = bounds["head_center"]
    eye_z = bounds["eye_center"].z
    # Band height centered around forehead/bangs for HairSpecTex angel-ring highlight
    hair_band_h = max(height * 1.35, 0.22)

    for p in me.polygons:
        role = role_by_slot.get(p.material_index, "cloth")
        if role in ("face", "eye", "lash"):
            for li in p.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                # Symmetrical frontal projection matching DanbaidongRP PBRToonFace.shader:
                # Blender +X is character Left (-X in Unity); UV1.x = 0.5 + x / width puts
                # character Right (x < 0) at UV1.x < 0.5 so 1 - UV1.x > 0.5 (lit when light is from right).
                u = min(0.995, max(0.005, 0.5 + v.x / width))
                vv = min(0.995, max(0.005, 0.04 + (v.z - chin) / height))
                uv1[li].uv = (u, vv)
                uv2[li].uv = (u, vv)
        elif role == "hair":
            for li in p.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                rel = v - head_c
                # Azimuth around head (front = -Y -> angle 0 -> u = 0.5)
                angle = math.atan2(rel.x, -rel.y)  # [-pi, pi]
                u = (angle / (2.0 * math.pi) + 0.5) % 1.0
                # Center vertical band (v = 0.5) near crown/upper bangs (~6cm above eye level)
                band_center_z = eye_z + 0.055
                vv = 0.5 + (v.z - band_center_z) / hair_band_h
                vv = min(0.98, max(0.02, vv))
                uv1[li].uv = (u, vv)
                uv2[li].uv = uv0[li].uv
        else:
            for li in p.loop_indices:
                uv1[li].uv = uv0[li].uv
                uv2[li].uv = uv0[li].uv


def blend_stylized_normals(me, role_by_slot, bounds, hair_blend: float = 0.62, face_blend: float = 0.35):
    """Apply soft spherical normal blending on hair (and gentle smoothing on face) per DanbaidongRP docs."""
    # Capture existing split normals before modification
    orig_normals = [Vector(l.normal).normalized() for l in me.loops]
    head_c = bounds["head_center"]
    eye_c = bounds["eye_center"]
    # Slightly behind face center for pleasing anime sphere shading
    face_sphere_c = Vector((0.0, eye_c.y + 0.065, eye_c.z - 0.005))
    hair_sphere_c = Vector((0.0, head_c.y + 0.015, head_c.z + 0.02))

    new_normals = list(orig_normals)
    for p in me.polygons:
        role = role_by_slot.get(p.material_index, "cloth")
        if role == "hair" and hair_blend > 0.0:
            for li in p.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                sdir = v - hair_sphere_c
                # Slightly flatten Z component so crown highlight wraps gracefully like anime hair
                sdir.z *= 0.80
                if sdir.length > 1e-5:
                    sdir.normalize()
                    blended = orig_normals[li].lerp(sdir, hair_blend).normalized()
                    new_normals[li] = blended
        elif role == "face" and face_blend > 0.0:
            for li in p.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                sdir = v - face_sphere_c
                if sdir.length > 1e-5:
                    sdir.normalize()
                    blended = orig_normals[li].lerp(sdir, face_blend).normalized()
                    new_normals[li] = blended

    me.normals_split_custom_set(new_normals)
    me.update()
    print(f"  Applied stylized normals (hair_blend={hair_blend:.2f}, face_blend={face_blend:.2f})", flush=True)


def bake_vertex_color_smooth_normals(me, role_by_slot):
    """Fast vectorized baking of smooth normals + per-part outline width mask into Vertex Color RGBA."""
    import numpy as np

    num_verts = len(me.vertices)
    num_loops = len(me.loops)
    num_polys = len(me.polygons)

    # Read vertex coordinates and loop vertex indices via fast C-level foreach_get
    coords = np.empty(num_verts * 3, dtype=np.float32)
    me.vertices.foreach_get("co", coords)
    coords = coords.reshape((num_verts, 3))

    loop_vids = np.empty(num_loops, dtype=np.int32)
    me.loops.foreach_get("vertex_index", loop_vids)

    poly_mat = np.empty(num_polys, dtype=np.int32)
    me.polygons.foreach_get("material_index", poly_mat)
    poly_lstart = np.empty(num_polys, dtype=np.int32)
    me.polygons.foreach_get("loop_start", poly_lstart)
    poly_ltotal = np.empty(num_polys, dtype=np.int32)
    me.polygons.foreach_get("loop_total", poly_ltotal)

    # Role -> outline width multiplier stored in vertex color alpha
    role_outline_alpha = {
        "face": 0.55,
        "eye": 0.0,
        "lash": 0.0,
        "transparent": 0.0,
        "hair": 0.90,
        "skin": 0.85,
        "stockings": 0.82,
        "cloth": 1.00,
        "metal": 1.00,
    }
    max_slot = max(int(poly_mat.max()) + 1, len(me.materials), 1)
    slot_alpha = np.array(
        [role_outline_alpha.get(role_by_slot.get(i, "cloth"), 0.95) for i in range(max_slot)],
        dtype=np.float32,
    )
    poly_alphas = slot_alpha[poly_mat]
    loop_alphas = np.repeat(poly_alphas, poly_ltotal)

    # Suppress interior face outlines around eyes/nose/mouth while keeping crisp outer cheek/chin/jawline silhouette!
    face_slots = [i for i, r in role_by_slot.items() if r == "face"]
    if face_slots:
        loop_mat = np.repeat(poly_mat, poly_ltotal)
        is_face_loop = np.isin(loop_mat, face_slots)
        f_coords = coords[loop_vids]
        # Front center of face (eyes/nose/mouth region): small |x| and forward -Y -> alpha = 0
        # Outer cheeks, jaw, chin, ears: larger |x| or back/lower -> alpha = 0.55
        if np.any(is_face_loop):
            face_pts = f_coords[is_face_loop]
            y_front = float(np.percentile(face_pts[:, 1], 15))
            z_chin = float(face_pts[:, 2].min())
            z_top = float(face_pts[:, 2].max())
            z_h = max(z_top - z_chin, 1e-4)
            in_center_features = (
                (np.abs(f_coords[:, 0]) < 0.052)
                & (f_coords[:, 1] < y_front + 0.045)
                & (f_coords[:, 2] > z_chin + 0.16 * z_h)
                & (f_coords[:, 2] < z_chin + 0.78 * z_h)
            )
            loop_alphas = np.where(is_face_loop & in_center_features, 0.0, loop_alphas)

    # Compute smooth normals per welded spatial position using polygon normals weighted by area
    poly_normals = np.empty(num_polys * 3, dtype=np.float32)
    me.polygons.foreach_get("normal", poly_normals)
    poly_normals = poly_normals.reshape((num_polys, 3))
    poly_areas = np.empty(num_polys, dtype=np.float32)
    me.polygons.foreach_get("area", poly_areas)
    weighted_pn = poly_normals * poly_areas[:, None]

    # Accumulate per vertex index
    v_smooth = np.zeros((num_verts, 3), dtype=np.float64)
    loop_pn = np.repeat(weighted_pn, poly_ltotal, axis=0)
    np.add.at(v_smooth, loop_vids, loop_pn)

    # Weld split vertices at identical spatial positions (quantized to 0.05 mm)
    q = np.round(coords * 20000.0).astype(np.int64)
    _, inv = np.unique(q, axis=0, return_inverse=True)
    num_unique = int(inv.max()) + 1
    u_smooth = np.zeros((num_unique, 3), dtype=np.float64)
    np.add.at(u_smooth, inv, v_smooth)
    norms = np.linalg.norm(u_smooth, axis=1, keepdims=True)
    u_smooth = np.where(norms > 1e-8, u_smooth / np.maximum(norms, 1e-8), np.array([0.0, 0.0, 1.0]))
    sn_loops = u_smooth[inv[loop_vids]].astype(np.float32)

    # Project into loop tangent space (Unity CharacterOutlineImport also refines RGB to Unity's exact MikkTSpace tangents while preserving A!)
    me.calc_tangents(uvmap=me.uv_layers[0].name)
    loop_n = np.empty(num_loops * 3, dtype=np.float32)
    me.loops.foreach_get("normal", loop_n)
    loop_n = loop_n.reshape((num_loops, 3))
    loop_t = np.empty(num_loops * 3, dtype=np.float32)
    me.loops.foreach_get("tangent", loop_t)
    loop_t = loop_t.reshape((num_loops, 3))
    loop_bsign = np.empty(num_loops, dtype=np.float32)
    me.loops.foreach_get("bitangent_sign", loop_bsign)
    loop_bt = np.cross(loop_n, loop_t) * loop_bsign[:, None]

    lx = np.sum(loop_t * sn_loops, axis=1)
    ly = np.sum(loop_bt * sn_loops, axis=1)
    lz = np.sum(loop_n * sn_loops, axis=1)
    local = np.stack([lx, ly, lz], axis=1)
    lnorms = np.linalg.norm(local, axis=1, keepdims=True)
    local = np.where(lnorms > 1e-8, local / np.maximum(lnorms, 1e-8), np.array([0.0, 0.0, 1.0], dtype=np.float32))

    rgba = np.empty((num_loops, 4), dtype=np.float32)
    rgba[:, :3] = np.clip(local * 0.5 + 0.5, 0.0, 1.0)
    rgba[:, 3] = loop_alphas

    while len(me.color_attributes) > 0:
        me.color_attributes.remove(me.color_attributes[0])
    col_attr = me.color_attributes.new(name="SmoothNormalOutline", type="BYTE_COLOR", domain="CORNER")
    col_attr.data.foreach_set("color", rgba.ravel())

    me.free_tangents()
    me.update()
    print(f"  Baked smooth normals + outline width mask into Vertex Color ({num_loops} corners)", flush=True)


def generate_sculpted_face_sdf(out_path: Path, size: int = 512):
    """Generate a high-res 512x512 sculpted anime Face SDF (DanbaidongRP R/G/B/A standard).

    R: Directional shadow SDF (right half > 0.5 lit when light is from right, left half < 0.5).
       Includes soft anime cheek crescent, brow ridge arch, and delicate nose-bridge contour.
    G & B: Subtle nose-tip specular SDF bounds.
    A: Clean face shadow mask (1.0 across face).
    """
    pixels = [0.0] * (size * size * 4)
    idx = 0
    for row in range(size):
        v = (row + 0.5) / size  # 0 = chin, 1 = hairline
        for col in range(size):
            u = (col + 0.5) / size  # 0 = left cheek, 1 = right cheek
            x = 2.0 * u - 1.0  # [-1, +1]

            # Smooth anime directional terminator:
            # - Linear horizontal sweep (0.5 + 0.44*x)
            # - Sculpted cheek curve (keeps cheeks softly lit longer, chin/forehead curve gently)
            # - Delicate anime nose ridge shadow indentation on the shadow side of center
            cheek_arch = 0.065 * math.exp(-((v - 0.36) / 0.24) ** 2) * (1.0 - x * x)
            forehead_dip = -0.095 * max(0.0, v - 0.58) ** 1.6
            chin_dip = -0.075 * max(0.0, 0.24 - v) ** 1.5
            # Classic Genshin/GFL2 anime nose-bridge contour: slight bump on lit side of nose
            nose_ridge = 0.095 * math.exp(-((v - 0.43) / 0.12) ** 2 - ((x - 0.09) / 0.17) ** 2)
            # Subtle eye-socket softening so eyes don't get harsh jagged shadow cuts
            eye_soft = 0.035 * math.exp(-((v - 0.52) / 0.09) ** 2 - ((abs(x) - 0.30) / 0.20) ** 2)

            sdf = 0.50 + 0.45 * x + nose_ridge + cheek_arch + eye_soft + forehead_dip + chin_dip
            sdf = min(0.985, max(0.015, sdf))

            # Subtle nose-tip highlight SDF in G & B (only active near nose bridge v~0.41, x~0)
            nose_spot = math.exp(-((v - 0.41) / 0.045) ** 2 - (x / 0.065) ** 2)
            g = min(1.0, max(0.0, 0.55 * nose_spot))
            b = min(1.0, max(0.0, 0.55 * nose_spot))

            pixels[idx] = sdf
            pixels[idx + 1] = g
            pixels[idx + 2] = b
            pixels[idx + 3] = 1.0
            idx += 4

    img = bpy.data.images.new("DanbaidongFaceSDF", width=size, height=size, alpha=True)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(pixels)
    img.filepath_raw = str(out_path.resolve())
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"  Generated 512x512 Face SDF -> {out_path}", flush=True)


def write_face_anchor(bounds, out_path: Path):
    eye = bounds["eye_center"]
    # Match cast.py Unity coordinate conversion: (-x, z, -y)
    unity_eye = (-eye.x, eye.z, -eye.y)
    out_path.write_text(" ".join(f"{v:.7f}" for v in unity_eye), encoding="ascii")
    print(f"  Wrote eye calibration -> {out_path}: {unity_eye}", flush=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("input_fbx")
    parser.add_argument("output_fbx")
    parser.add_argument("--actor", default="")
    parser.add_argument("--profile", default="")
    parser.add_argument("--out-dir", default="")
    args = parser.parse_args(argv)

    in_path = Path(args.input_fbx).resolve()
    out_path = Path(args.output_fbx).resolve()
    out_dir = Path(args.out_dir).resolve() if args.out_dir else out_path.parent
    actor = args.actor or out_path.stem
    profile_cfg = PROFILES.get(args.profile.lower(), {})

    print(f"[DanbaidongPrep] Processing {in_path} -> {out_path} (actor={actor}, profile={args.profile})", flush=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(in_path), use_custom_normals=True, ignore_leaf_bones=False)

    mesh_objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if not mesh_objs:
        raise RuntimeError(f"No mesh found in {in_path}")

    body = max(mesh_objs, key=lambda o: len(o.data.vertices))
    orig_verts = len(body.data.vertices)
    orig_shapes = list(body.data.shape_keys.key_blocks.keys()) if body.data.shape_keys else []
    print(f"  Body mesh: {body.name} ({orig_verts} verts, shapes={orig_shapes})", flush=True)

    # 1. Material sub-region splitting (zero vertex changes)
    apply_material_splits(body, profile_cfg.get("splits"))

    role_by_slot = get_role_map(body.data, profile_cfg)
    print("  Material roles:", [(m.name, role_by_slot[i]) for i, m in enumerate(body.data.materials)], flush=True)

    # 2. Compute face/head bounds & write UV1/UV2
    bounds = compute_face_and_head_bounds(body.data, role_by_slot)
    write_uvs_for_danbaidong(body.data, role_by_slot, bounds)

    # 3. Blend stylized spherical normals on hair (and gentle face smoothing)
    blend_stylized_normals(
        body.data,
        role_by_slot,
        bounds,
        hair_blend=profile_cfg.get("hair_sphere_blend", 0.60),
        face_blend=profile_cfg.get("face_normal_blend", 0.30),
    )

    # 4. Bake smooth normals into Vertex Color RGB + outline width mask into A
    bake_vertex_color_smooth_normals(body.data, role_by_slot)

    # 5. Generate Face SDF map & eye anchor file
    generate_sculpted_face_sdf(out_dir / f"{actor}FaceSDF.png", size=512)
    face_txt = out_dir / f"{actor}Face.txt"
    if not face_txt.exists():
        write_face_anchor(bounds, face_txt)

    assert len(body.data.vertices) == orig_verts, "Vertex count changed unexpectedly!"

    # 6. Export FBX preserving armature, shape keys, linear vertex colors, custom normals, and UVs
    bpy.ops.object.select_all(action="DESELECT")
    export_objs = list(armatures) + list(mesh_objs)
    for o in export_objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = armatures[0] if armatures else body

    has_shapes = any(o.type == "MESH" and o.data.shape_keys for o in export_objs)
    bpy.ops.export_scene.fbx(
        filepath=str(out_path),
        use_selection=True,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        path_mode="STRIP",
        mesh_smooth_type="FACE",
        use_mesh_modifiers=not has_shapes,
        use_custom_props=False,
        bake_anim=False,
        colors_type="LINEAR",
    )
    print(f"[DanbaidongPrep] Done! Exported {out_path}", flush=True)


if __name__ == "__main__":
    main()
