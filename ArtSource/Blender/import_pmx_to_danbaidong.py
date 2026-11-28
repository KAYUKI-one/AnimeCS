"""
Pure-Python PMX 2.0 -> Blender Humanoid FBX + DanbaidongRP Prep Converter (Blender 4.x / 5.x).
Zero external Blender addon required (works cleanly even when mmd_tools is unavailable).

Features:
  - Parses PMX 2.0 vertices, normals, UVs, skin weights (BDEF1/BDEF2/BDEF4/SDEF), bones, materials
  - Maps Japanese/Chinese/GFL2 bone names into Unity Humanoid skeleton (Hips/Spine/Chest/Neck/Head/Arms/Legs/Fingers)
    plus SpringHair_* / SpringCloth_* secondary-motion chains
  - Copies diffuse textures AND auto-matches native GFL2/PBR Normal Maps (*_n.png -> <Mat>_Normal.png),
    RMO Maps (*_rmo*.png -> <Mat>_RMO.png), and Hair Spec Maps (*_hair_spc*.png -> <Mat>_HairSpec.png)
  - Runs DanbaidongRP UV1/UV2, stylized sphere normals, vertex-color smooth outline normals + Face jawline mask,
    and 512x512 Sculpted Face SDF + Face.txt automatically!

Usage:
  blender -b --python import_pmx_to_danbaidong.py -- <input.pmx> <output_dir> --actor Mishty
"""
import argparse
import math
import os
import re
import shutil
import struct
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).parent))
import danbaidong_character_prep as prep


class PmxReader:
    def __init__(self, data: bytes):
        self.data = data
        self.pos = 0
        magic = self.read_bytes(4)
        if magic != b"PMX ":
            raise ValueError(f"Not a PMX file: {magic}")
        self.version = self.read_fmt("<f")[0]
        gcount = self.read_fmt("<B")[0]
        self.globals = self.read_fmt(f"<{gcount}B")
        self.enc = "utf-16-le" if self.globals[0] == 0 else "utf-8"
        self.add_uv = self.globals[1]
        self.v_sz = self.globals[2]
        self.t_sz = self.globals[3]
        self.m_sz = self.globals[4]
        self.b_sz = self.globals[5]
        self.mo_sz = self.globals[6]
        self.r_sz = self.globals[7]

        self.name_jp = self.read_text()
        self.name_en = self.read_text()
        self.comment_jp = self.read_text()
        self.comment_en = self.read_text()

    def read_bytes(self, n):
        b = self.data[self.pos:self.pos + n]
        self.pos += n
        return b

    def read_fmt(self, fmt):
        sz = struct.calcsize(fmt)
        val = struct.unpack_from(fmt, self.data, self.pos)
        self.pos += sz
        return val

    def read_text(self):
        n = self.read_fmt("<i")[0]
        return self.read_bytes(n).decode(self.enc, errors="replace")

    def read_idx(self, sz, unsigned=False):
        if sz == 1:
            return self.read_fmt("<B" if unsigned else "<b")[0]
        if sz == 2:
            return self.read_fmt("<H" if unsigned else "<h")[0]
        return self.read_fmt("<I" if unsigned else "<i")[0]

    def parse(self):
        # 1. Vertices
        v_count = self.read_fmt("<i")[0]
        pos = np.empty((v_count, 3), dtype=np.float32)
        nrm = np.empty((v_count, 3), dtype=np.float32)
        uv = np.empty((v_count, 2), dtype=np.float32)
        b_idx = np.full((v_count, 4), -1, dtype=np.int32)
        b_w = np.zeros((v_count, 4), dtype=np.float32)

        b_fmt = {1: "<b", 2: "<h", 4: "<i"}[self.b_sz]
        for i in range(v_count):
            px, py, pz, nx, ny, nz, u, v = self.read_fmt("<8f")
            pos[i] = (px, py, pz)
            nrm[i] = (nx, ny, nz)
            uv[i] = (u, 1.0 - v)  # PMX V-top -> Blender/Unity V-bottom
            if self.add_uv:
                self.pos += 16 * self.add_uv
            wtype = self.read_fmt("<B")[0]
            if wtype == 0:  # BDEF1
                b0 = self.read_fmt(b_fmt)[0]
                b_idx[i, 0] = b0
                b_w[i, 0] = 1.0
            elif wtype == 1:  # BDEF2
                b0 = self.read_fmt(b_fmt)[0]
                b1 = self.read_fmt(b_fmt)[0]
                w0 = self.read_fmt("<f")[0]
                b_idx[i, 0], b_idx[i, 1] = b0, b1
                b_w[i, 0], b_w[i, 1] = w0, 1.0 - w0
            elif wtype == 2:  # BDEF4
                b0, b1, b2, b3 = (self.read_fmt(b_fmt)[0] for _ in range(4))
                w0, w1, w2, w3 = self.read_fmt("<4f")
                b_idx[i] = (b0, b1, b2, b3)
                b_w[i] = (w0, w1, w2, w3)
            elif wtype == 3:  # SDEF
                b0 = self.read_fmt(b_fmt)[0]
                b1 = self.read_fmt(b_fmt)[0]
                w0 = self.read_fmt("<f")[0]
                self.pos += 36  # C, R0, R1
                b_idx[i, 0], b_idx[i, 1] = b0, b1
                b_w[i, 0], b_w[i, 1] = w0, 1.0 - w0
            else:  # QDEF
                b0, b1, b2, b3 = (self.read_fmt(b_fmt)[0] for _ in range(4))
                w0, w1, w2, w3 = self.read_fmt("<4f")
                b_idx[i] = (b0, b1, b2, b3)
                b_w[i] = (w0, w1, w2, w3)
            self.pos += 4  # edge scale

        # 2. Faces (indices)
        f_idx_count = self.read_fmt("<i")[0]
        v_dtype = {1: np.uint8, 2: np.uint16, 4: np.uint32}[self.v_sz]
        faces = np.frombuffer(self.data, dtype=v_dtype, count=f_idx_count, offset=self.pos).reshape((-1, 3)).astype(np.int32)
        self.pos += f_idx_count * self.v_sz

        # 3. Textures
        t_count = self.read_fmt("<i")[0]
        textures = [self.read_text() for _ in range(t_count)]

        # 4. Materials
        m_count = self.read_fmt("<i")[0]
        materials = []
        for _ in range(m_count):
            m_jp = self.read_text()
            m_en = self.read_text()
            diffuse = self.read_fmt("<4f")
            self.pos += 16 + 12 + 1 + 16 + 4  # spec(12)+specpow(4)+amb(12)+flag(1)+edgecol(16)+edgesize(4)
            tex_idx = self.read_idx(self.t_sz, unsigned=False)
            self.read_idx(self.t_sz, unsigned=False)  # sph
            self.pos += 1  # sph mode
            toon_flag = self.read_fmt("<B")[0]
            if toon_flag == 0:
                self.read_idx(self.t_sz, unsigned=False)
            else:
                self.pos += 1
            self.read_text()  # memo
            face_vert_count = self.read_fmt("<i")[0]
            materials.append({
                "name_jp": m_jp,
                "name_en": m_en,
                "diffuse": diffuse,
                "tex": textures[tex_idx] if 0 <= tex_idx < len(textures) else "",
                "tri_count": face_vert_count // 3,
            })

        # 5. Bones
        b_count = self.read_fmt("<i")[0]
        bones = []
        for _ in range(b_count):
            bn_jp = self.read_text()
            bn_en = self.read_text()
            bpos = self.read_fmt("<3f")
            parent = self.read_idx(self.b_sz, unsigned=False)
            self.pos += 4  # layer
            flags = self.read_fmt("<H")[0]
            if flags & 0x0001:
                tail_idx = self.read_idx(self.b_sz, unsigned=False)
                tail_off = None
            else:
                tail_idx = -1
                tail_off = self.read_fmt("<3f")
            if (flags & 0x0100) or (flags & 0x0200):
                self.read_idx(self.b_sz, unsigned=False)
                self.pos += 4
            if flags & 0x0400:
                self.pos += 12
            if flags & 0x0800:
                self.pos += 24
            if flags & 0x2000:
                self.pos += 4
            if flags & 0x0020:  # IK
                self.read_idx(self.b_sz, unsigned=False)
                self.pos += 8
                ik_links = self.read_fmt("<i")[0]
                for _ in range(ik_links):
                    self.read_idx(self.b_sz, unsigned=False)
                    lim = self.read_fmt("<B")[0]
                    if lim:
                        self.pos += 24
            bones.append({
                "name_jp": bn_jp,
                "name_en": bn_en,
                "pos": bpos,
                "parent": parent,
                "tail_idx": tail_idx,
                "tail_off": tail_off,
            })

        return pos, nrm, uv, b_idx, b_w, faces, materials, bones


# Standard PMX Japanese -> Unity Humanoid Bone Name Map
HUMANOID_MAP = {
    "センター": "Root",
    "グルーブ": "Root",
    "腰": "Hips",
    "下半身": "Hips",
    "上半身": "Spine",
    "上半身2": "Chest",
    "上半身3": "UpperChest",
    "首": "Neck",
    "頭": "Head",
    "左目": "LeftEye",
    "右目": "RightEye",
    "左肩": "LeftShoulder",
    "左腕": "LeftUpperArm",
    "左ひじ": "LeftLowerArm",
    "左手首": "LeftHand",
    "右肩": "RightShoulder",
    "右腕": "RightUpperArm",
    "右ひじ": "RightLowerArm",
    "右手首": "RightHand",
    "左足": "LeftUpperLeg",
    "左足D": "LeftUpperLeg",
    "左ひざ": "LeftLowerLeg",
    "左ひざD": "LeftLowerLeg",
    "左足首": "LeftFoot",
    "左足首D": "LeftFoot",
    "左つま先": "LeftToes",
    "左足先EX": "LeftToes",
    "右足": "RightUpperLeg",
    "右足D": "RightUpperLeg",
    "右ひざ": "RightLowerLeg",
    "右ひざD": "RightLowerLeg",
    "右足首": "RightFoot",
    "右足首D": "RightFoot",
    "右つま先": "RightToes",
    "右足先EX": "RightToes",
}
for side_jp, side_en in (("左", "Left"), ("右", "Right")):
    for f_jp, f_en in (("親指", "Thumb"), ("人指", "Index"), ("中指", "Middle"), ("薬指", "Ring"), ("小指", "Little")):
        for j_jp, j_en in (("０", "Proximal"), ("１", "Proximal"), ("２", "Intermediate"), ("３", "Distal")):
            if f_jp == "親指" and j_jp == "１":
                j_en = "Intermediate"
            elif f_jp == "親指" and j_jp == "２":
                j_en = "Distal"
            HUMANOID_MAP[side_jp + f_jp + j_jp] = side_en + f_en + j_en


def pmx_to_blender(v):
    # PMX: +X=Right (character left), +Y=Up, -Z=Front -> Blender: +X=Left, -Y=Front, +Z=Up
    return np.stack([v[:, 0], v[:, 2], v[:, 1]], axis=1)


def find_matching_map(pmx_dir: Path, tex_rel: str, suffixes):
    if not tex_rel:
        return None
    stem = Path(tex_rel.replace("\\", "/")).stem
    # Strip trailing _d / _da / _purple etc. to find base stem (e.g. c_MishtySSR0101_slg_cloth1)
    base = re.sub(r"(_da|_d|_purple|_diff|_albedo)$", "", stem, flags=re.IGNORECASE)
    for search_dir in (pmx_dir / "normalmap", pmx_dir / "Textures", pmx_dir):
        if not search_dir.exists():
            continue
        for f in search_dir.iterdir():
            if not f.is_file():
                continue
            fl = f.name.lower()
            for suf in suffixes:
                if fl.startswith(base.lower() + suf.lower()):
                    return f
    return None


def classify_material_suffix(m_name: str, tex_name: str) -> str:
    s = (m_name + " " + tex_name).lower()
    if any(k in s for k in ("eyeblend", "eyeglow", "eyehighlight", "glass", "transparent")):
        return "Transparent"
    if any(k in s for k in ("lash", "brow", "睫", "眉")):
        return "Lash"
    if any(k in s for k in ("eye", "瞳", "目")):
        return "Eye"
    if any(k in s for k in ("face", "head", "脸", "顔")):
        return "Face"
    if any(k in s for k in ("hair", "发", "髪")):
        return "Hair"
    if any(k in s for k in ("body_d", "skin", "肌")):
        return "Skin"
    if any(k in s for k in ("sock", "stocking", "丝袜")):
        return "Stockings"
    return "Cloth"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input_pmx")
    parser.add_argument("output_dir")
    parser.add_argument("--actor", default="Mishty")
    parser.add_argument("--target_height", type=float, default=1.64)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])

    pmx_path = Path(args.input_pmx)
    pmx_dir = pmx_path.parent
    out_dir = Path(args.output_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    actor = args.actor

    print(f"[PMX->Danbaidong] Parsing {pmx_path.name}...", flush=True)
    reader = PmxReader(pmx_path.read_bytes())
    pos, nrm, uv, b_idx, b_w, faces, materials, bones = reader.parse()
    print(f"  Loaded {len(pos)} verts, {len(faces)} tris, {len(materials)} mats, {len(bones)} bones", flush=True)

    # Convert to Blender coordinate frame and normalize height to target_height
    bpos = pmx_to_blender(pos)
    bnrm = pmx_to_blender(nrm)
    z_min, z_max = float(bpos[:, 2].min()), float(bpos[:, 2].max())
    scale = args.target_height / max(z_max - z_min, 1e-4)
    bpos = (bpos - np.array([0.0, 0.0, z_min], dtype=np.float32)) * scale

    # Flip triangle winding because Z/Y axis swap reverses handedness
    faces_bl = faces[:, [0, 2, 1]]

    bpy.ops.wm.read_factory_settings(use_empty=True)

    # Create Armature with clean Unity Humanoid + SpringHair_/SpringCloth_ bone naming
    arm_data = bpy.data.armatures.new(f"{actor}Skeleton")
    arm_obj = bpy.data.objects.new("Skeleton", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")

    used_names = {}
    bone_final_names = []
    edit_bones = []
    for idx, bd in enumerate(bones):
        raw_jp = bd["name_jp"]
        mapped = HUMANOID_MAP.get(raw_jp)
        if mapped and mapped not in used_names:
            name = mapped
        else:
            nl = (raw_jp + " " + bd["name_en"]).lower()
            if any(k in nl for k in ("hair", "髪", "发", "耳", "ahoge")):
                prefix = "SpringHair_"
            elif any(k in nl for k in ("skirt", "coat", "cloth", "ribbon", "tie", "sleeve", "裙", "衣", "带", "袖")):
                prefix = "SpringCloth_"
            else:
                prefix = "Bone_"
            clean = re.sub(r"[^A-Za-z0-9_]", "_", bd["name_en"] or f"B{idx}")
            name = f"{prefix}{clean}_{idx}"
        used_names[name] = idx
        bone_final_names.append(name)

        eb = arm_data.edit_bones.new(name)
        p = np.array(bd["pos"], dtype=np.float32)
        head = Vector((p[0] * scale, p[2] * scale, (p[1] - z_min) * scale))
        eb.head = head
        eb.tail = head + Vector((0.0, 0.0, 0.04))
        edit_bones.append(eb)

    for idx, bd in enumerate(bones):
        eb = edit_bones[idx]
        pidx = bd["parent"]
        if 0 <= pidx < len(edit_bones) and pidx != idx:
            eb.parent = edit_bones[pidx]
        tidx = bd["tail_idx"]
        if 0 <= tidx < len(edit_bones) and (edit_bones[tidx].head - eb.head).length > 1e-4:
            eb.tail = edit_bones[tidx].head
        elif bd["tail_off"] is not None:
            off = Vector((bd["tail_off"][0] * scale, bd["tail_off"][2] * scale, bd["tail_off"][1] * scale))
            if off.length > 1e-4:
                eb.tail = eb.head + off
    bpy.ops.object.mode_set(mode="OBJECT")

    # Create Mesh
    me = bpy.data.meshes.new(f"{actor}Body")
    me.from_pydata(bpos.tolist(), [], faces_bl.tolist())
    me.update()

    # Set UV0
    uv_layer = me.uv_layers.new(name="UV0")
    loop_vids = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", loop_vids)
    uv_layer.data.foreach_set("uv", uv[loop_vids].ravel())

    # Assign Materials & copy textures (Diffuse + Native GFL2 Normal + Native GFL2 RMO + Native HairSpec!)
    mat_groups = {}
    poly_mat_indices = np.zeros(len(me.polygons), dtype=np.int32)
    tri_cursor = 0
    for mi, md in enumerate(materials):
        tri_end = min(len(me.polygons), tri_cursor + md["tri_count"])
        tex_rel = md["tex"].replace("\\", "/")
        role_tag = classify_material_suffix(md["name_jp"] + "_" + md["name_en"], tex_rel)
        key = (tex_rel.lower(), role_tag)
        if key not in mat_groups:
            slot_idx = len(mat_groups)
            mat_name = f"{actor}_{role_tag}_{slot_idx:02d}"
            mat_groups[key] = (slot_idx, mat_name)
            bmat = bpy.data.materials.new(mat_name)
            me.materials.append(bmat)

            # Copy diffuse texture
            if tex_rel:
                src_tex = pmx_dir / tex_rel
                if not src_tex.exists():
                    cands = list(pmx_dir.rglob(Path(tex_rel).name))
                    src_tex = cands[0] if cands else None
                if src_tex and src_tex.is_file():
                    shutil.copyfile(src_tex, out_dir / f"{mat_name}.png")
                    # Copy native GFL2 Normal Map (*_n.png -> <Mat>_Normal.png)
                    norm_src = find_matching_map(pmx_dir, tex_rel, ["_n.", "_normal."])
                    if norm_src:
                        shutil.copyfile(norm_src, out_dir / f"{mat_name}_Normal.png")
                    # Copy native GFL2 RMO Map (*_rmo*.png -> <Mat>_RMO.png)
                    rmo_src = find_matching_map(pmx_dir, tex_rel, ["_rmo"])
                    if rmo_src:
                        shutil.copyfile(rmo_src, out_dir / f"{mat_name}_RMO.png")
                    # Copy native GFL2 Hair Spec Map (*_spc*.png -> <Mat>_HairSpec.png)
                    if role_tag == "Hair":
                        spc_src = find_matching_map(pmx_dir, tex_rel, ["_spc", "_spec"])
                        if spc_src:
                            shutil.copyfile(spc_src, out_dir / f"{mat_name}_HairSpec.png")
        slot_idx = mat_groups[key][0]
        poly_mat_indices[tri_cursor:tri_end] = slot_idx
        tri_cursor = tri_end

    me.polygons.foreach_set("material_index", poly_mat_indices)
    me.polygons.foreach_set("use_smooth", np.ones(len(me.polygons), dtype=bool))

    # Apply imported PMX custom normals cleanly
    loop_nrm = bnrm[loop_vids]
    ln = np.linalg.norm(loop_nrm, axis=1, keepdims=True)
    loop_nrm = np.where(ln > 1e-6, loop_nrm / np.maximum(ln, 1e-6), np.array([0.0, 0.0, 1.0], dtype=np.float32))
    me.normals_split_custom_set(loop_nrm.tolist())

    mesh_obj = bpy.data.objects.new(f"{actor}Body", me)
    bpy.context.collection.objects.link(mesh_obj)
    mesh_obj.parent = arm_obj
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj

    # Assign skin weights
    vgroups = [mesh_obj.vertex_groups.new(name=bn) for bn in bone_final_names]
    for vi in range(len(pos)):
        ws = b_w[vi]
        ids = b_idx[vi]
        total = float(sum(ws[k] for k in range(4) if ids[k] >= 0 and ws[k] > 1e-4))
        if total <= 1e-5:
            continue
        for k in range(4):
            bi = int(ids[k])
            if 0 <= bi < len(vgroups) and ws[k] > 1e-4:
                vgroups[bi].add([vi], float(ws[k]) / total, "REPLACE")

    # Now run our unified DanbaidongRP prep pipeline directly on this mesh!
    role_by_slot = {i: prep.default_role(m.name) for i, m in enumerate(me.materials)}
    print(f"  Material roles: {[(me.materials[i].name, role_by_slot[i]) for i in range(len(me.materials))]}", flush=True)
    bounds = prep.compute_face_and_head_bounds(me, role_by_slot)
    prep.write_uvs_for_danbaidong(me, role_by_slot, bounds)
    prep.blend_stylized_normals(me, role_by_slot, bounds, hair_blend=0.58, face_blend=0.32)
    prep.bake_vertex_color_smooth_normals(me, role_by_slot)
    prep.generate_sculpted_face_sdf(out_dir / f"{actor}FaceSDF.png", size=512)
    prep.write_face_anchor(bounds, out_dir / f"{actor}Face.txt")

    out_fbx = out_dir / f"{actor}.fbx"
    bpy.ops.object.select_all(action="DESELECT")
    arm_obj.select_set(True)
    mesh_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.export_scene.fbx(
        filepath=str(out_fbx),
        use_selection=True,
        use_mesh_modifiers=False,
        bake_anim=False,
        add_leaf_bones=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        path_mode="STRIP",
        mesh_smooth_type="FACE",
        use_custom_props=False,
        colors_type="LINEAR",
    )
    print(f"[PMX->Danbaidong] Done! Exported {out_fbx} with native GFL2 Normal/RMO/HairSpec maps!", flush=True)


if __name__ == "__main__":
    main()
