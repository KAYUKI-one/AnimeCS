"""
Graceful Anime RPG Humanoid Showcase Animations Generator (Blender 4.x / 5.x).
Usage:
  blender -b --python anime_showcase_motions.py -- <donor_rig.fbx> <output_motions.fbx>

Authors clean, natural, anatomically proportioned Anime RPG animations (zero FPS crouch/invisible-rifle):
  - AnimeIdle : Upright graceful breathing stance with subtle contrapposto & relaxed arms/fingers
  - AnimeWalk : Natural feminine anime RPG walk with counter-swinging arms & heel-toe foot roll
  - AnimeRun  : Energetic anime RPG run (like Wuthering Waves / GFL2) with natural side arm pump
  - AnimePose : Soft anime figurine showcase pose with gentle hip tilt & subtle head tilt
"""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


def _set_bone(rig, rest, heads, rotations, shift, name, world=None, target=None, tip=None, frame=None):
    if name not in rest:
        return
    b = rest[name]
    p = rig.pose.bones[name]
    parent = rotations[b.parent.name] if b.parent and b.parent.name in rotations else Quaternion()
    original = b.matrix_local.to_quaternion()

    if b.parent is None or b.parent.name not in heads:
        h = b.head_local + shift
    else:
        h = heads[b.parent.name] + rotations[b.parent.name] @ (b.head_local - b.parent.head_local)

    if target is not None:
        ray = (rest[tip].head_local if tip and tip in rest else b.tail_local) - b.head_local
        if ray.length > 1e-6 and (target - h).length > 1e-6:
            world = ray.rotation_difference(target - h)
    if world is None:
        world = parent

    local = original.inverted() @ parent.inverted() @ world @ original
    if p.rotation_quaternion.dot(local) < 0:
        local.negate()
    p.rotation_mode = "QUATERNION"
    p.rotation_quaternion = local
    if b.parent is None:
        p.location = original.inverted() @ shift
    heads[name] = h
    rotations[name] = world

    if frame is not None:
        p.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=name)
        if b.parent is None:
            p.keyframe_insert(data_path="location", frame=frame, group=name)


def _two_bone(rig, rest, heads, rotations, shift, a, b, c, goal, hint, frame=None):
    if a not in rest or b not in rest or c not in rest:
        return
    start = heads[rest[a].parent.name] + rotations[rest[a].parent.name] @ (rest[a].head_local - rest[a].parent.head_local)
    line = goal - start
    if line.length < 1e-5:
        return
    axis = line.normalized()
    la = (rest[b].head_local - rest[a].head_local).length
    lb = (rest[c].head_local - rest[b].head_local).length
    d = min(la + lb - 0.002, max(abs(la - lb) + 0.002, line.length))
    along = (la * la - lb * lb + d * d) / (2.0 * d)
    bend = hint - axis * hint.dot(axis)
    if bend.length < 1e-5:
        bend = Vector((0.0, -1.0, 0.0))
    else:
        bend.normalize()
    elbow = start + axis * along + bend * math.sqrt(max(0.0, la * la - along * along))
    _set_bone(rig, rest, heads, rotations, shift, a, target=elbow, tip=b, frame=frame)
    _set_bone(rig, rest, heads, rotations, shift, b, target=start + axis * d, tip=c, frame=frame)


def _pose_fingers(rig, rest, heads, rotations, shift, side, hand_rot, curl_base, frame):
    for finger in ("Thumb", "Index", "Middle", "Ring", "Little"):
        f_scale = 0.25 if finger == "Thumb" else 0.75 if finger == "Index" else 0.95 if finger == "Middle" else 1.12 if finger == "Ring" else 1.25
        for j, joint in enumerate(("Proximal", "Intermediate", "Distal")):
            name = side + finger + joint
            if name in rest:
                angle = curl_base * f_scale * (j + 1)
                axis = Vector((0.0, 0.35 if side == "Left" else -0.35, 1.0)).normalized() if finger == "Thumb" else Vector((1.0, 0.0, 0.0))
                _set_bone(rig, rest, heads, rotations, shift, name, Quaternion(axis, angle) @ hand_rot, frame=frame)


def pose_anime(rig, action_name, frac, frame):
    rest = rig.data.bones
    heads, rotations = {}, {}
    tau = frac * 2.0 * math.pi
    s = math.sin(tau)
    c = math.cos(tau)

    # Coordinate convention of AnimeCS Blender rig: +X = Left, -Y = Front, +Z = Up
    if action_name == "AnimeIdle":
        breath = 0.5 - 0.5 * c
        dip = -0.008 + 0.005 * breath
        hip_x = 0.014 + 0.005 * s
        shift = Vector((hip_x, 0.0, dip))

        hip_rot = Quaternion((0, 1, 0), 0.032 + 0.008 * s) @ Quaternion((0, 0, 1), -0.03)
        _set_bone(rig, rest, heads, rotations, shift, "Hips", hip_rot, frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Spine", Quaternion((1, 0, 0), 0.015 - 0.01 * breath) @ Quaternion((0, 1, 0), -0.015), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Chest", Quaternion((1, 0, 0), -0.012 - 0.016 * breath) @ Quaternion((0, 1, 0), -0.018), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "UpperChest", Quaternion((1, 0, 0), -0.010 - 0.012 * breath), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Neck", Quaternion((0, 1, 0), 0.015 * s), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Head", Quaternion((1, 0, 0), 0.012 * breath) @ Quaternion((0, 1, 0), 0.025 + 0.012 * s) @ Quaternion((0, 0, 1), 0.025 * math.sin(tau + 0.5)), frame=frame)

        # Graceful natural stance (feet close, slight feminine stagger, straight upright legs!)
        for side, sign in (("Left", 1.0), ("Right", -1.0)):
            u, l, f, t = [side + k for k in ("UpperLeg", "LowerLeg", "Foot", "Toes")]
            foot_goal = rest[f].head_local.copy()
            foot_goal.x += sign * 0.012
            foot_goal.y += -0.025 if sign > 0 else 0.018
            _two_bone(rig, rest, heads, rotations, shift, u, l, f, foot_goal, Vector((sign * 0.04, -1.0, 0.0)), frame=frame)
            _set_bone(rig, rest, heads, rotations, shift, f, Quaternion((0, 0, 1), sign * 0.07), frame=frame)
            _set_bone(rig, rest, heads, rotations, shift, t, Quaternion((0, 0, 1), sign * 0.07), frame=frame)

            # Relaxed natural arms hanging gracefully slightly away from skirt/hips
            _set_bone(rig, rest, heads, rotations, shift, side + "Shoulder", Quaternion((0, 1, 0), sign * (-0.03 - 0.012 * breath)), frame=frame)
            ua, la, hd = [side + k for k in ("UpperArm", "LowerArm", "Hand")]
            ua_pos = heads[rest[ua].parent.name] + rotations[rest[ua].parent.name] @ (rest[ua].head_local - rest[ua].parent.head_local)
            arm_len = (rest[la].head_local - rest[ua].head_local).length + (rest[hd].head_local - rest[la].head_local).length
            hand_goal = ua_pos + Vector((sign * 0.145, -0.035 + 0.008 * s, -arm_len * 0.945))
            _two_bone(rig, rest, heads, rotations, shift, ua, la, hd, hand_goal, Vector((sign * 0.45, 0.85, -0.15)), frame=frame)
            hand_rot = Quaternion((0, 1, 0), sign * -0.14) @ Quaternion((1, 0, 0), -0.08) @ rotations[la]
            _set_bone(rig, rest, heads, rotations, shift, hd, hand_rot, frame=frame)
            _pose_fingers(rig, rest, heads, rotations, shift, side, hand_rot, 0.16, frame)

    elif action_name == "AnimeWalk":
        # Light, graceful anime RPG walk (upright, natural arm pendulum, heel-toe stride)
        bob = 0.020 * abs(math.sin(tau)) - 0.018
        sway_x = 0.024 * math.cos(tau)
        shift = Vector((sway_x, 0.0, bob))

        hip_yaw = 0.085 * math.cos(tau)
        hip_roll = -0.045 * math.cos(tau)
        _set_bone(rig, rest, heads, rotations, shift, "Hips", Quaternion((0, 0, 1), hip_yaw) @ Quaternion((0, 1, 0), hip_roll), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Spine", Quaternion((1, 0, 0), 0.045) @ Quaternion((0, 0, 1), -hip_yaw * 0.45), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Chest", Quaternion((1, 0, 0), 0.035) @ Quaternion((0, 0, 1), -hip_yaw * 0.85), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "UpperChest", Quaternion((1, 0, 0), 0.02) @ Quaternion((0, 0, 1), -hip_yaw * 0.95), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Neck", Quaternion((1, 0, 0), -0.02), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Head", Quaternion((1, 0, 0), -0.03) @ Quaternion((0, 1, 0), 0.015 * math.cos(tau)), frame=frame)

        for side, sign in (("Left", 1.0), ("Right", -1.0)):
            phase = tau + (0.0 if sign > 0 else math.pi)
            # Forward is -Y: as phase increases from 0 -> pi (sin(phase) > 0, foot lifted),
            # cos(phase) goes +1 -> -1, so stride_y = +0.22 * cos(phase) swings foot FORWARD (+Y -> -Y) in air
            # and pushes BACKWARD (-Y -> +Y) while planted on the ground!
            stride_y = 0.22 * math.cos(phase)
            lift_z = max(0.0, math.sin(phase)) * 0.078
            u, l, f, t = [side + k for k in ("UpperLeg", "LowerLeg", "Foot", "Toes")]
            foot_goal = rest[f].head_local.copy()
            foot_goal.x += sign * -0.012  # graceful narrow anime stride line
            foot_goal.y += stride_y
            foot_goal.z += lift_z
            _two_bone(rig, rest, heads, rotations, shift, u, l, f, foot_goal, Vector((sign * 0.05, -1.0, 0.0)), frame=frame)
            foot_pitch = -0.20 * math.sin(phase) if math.sin(phase) > 0 else -0.10 * math.cos(phase)
            _set_bone(rig, rest, heads, rotations, shift, f, Quaternion((1, 0, 0), foot_pitch), frame=frame)
            _set_bone(rig, rest, heads, rotations, shift, t, Quaternion((1, 0, 0), max(0.0, -foot_pitch * 0.5)), frame=frame)

            # Natural opposite arm swing at sides (swings forward when opposite leg swings forward)
            arm_phase = phase + math.pi
            swing_y = 0.175 * math.cos(arm_phase)
            lift_arm_z = 0.035 * max(0.0, -math.cos(arm_phase))
            _set_bone(rig, rest, heads, rotations, shift, side + "Shoulder", Quaternion((0, 0, 1), 0.04 * math.cos(arm_phase)), frame=frame)
            ua, la, hd = [side + k for k in ("UpperArm", "LowerArm", "Hand")]
            ua_pos = heads[rest[ua].parent.name] + rotations[rest[ua].parent.name] @ (rest[ua].head_local - rest[ua].parent.head_local)
            arm_len = (rest[la].head_local - rest[ua].head_local).length + (rest[hd].head_local - rest[la].head_local).length
            hand_goal = ua_pos + Vector((sign * 0.145, swing_y - 0.02, -arm_len * 0.90 + lift_arm_z))
            _two_bone(rig, rest, heads, rotations, shift, ua, la, hd, hand_goal, Vector((sign * 0.32, 0.92, -0.10)), frame=frame)
            hand_rot = Quaternion((1, 0, 0), -0.10 * math.sin(arm_phase)) @ rotations[la]
            _set_bone(rig, rest, heads, rotations, shift, hd, hand_rot, frame=frame)
            _pose_fingers(rig, rest, heads, rotations, shift, side, hand_rot, 0.19, frame)

    elif action_name == "AnimeRun":
        # Energetic athletic anime RPG run (natural bent-elbow side pump — ZERO rifle pose!)
        bob = 0.036 * abs(math.sin(tau)) - 0.035
        sway_x = 0.020 * math.cos(tau)
        shift = Vector((sway_x, 0.0, bob))

        hip_yaw = 0.12 * math.cos(tau)
        _set_bone(rig, rest, heads, rotations, shift, "Hips", Quaternion((1, 0, 0), 0.10) @ Quaternion((0, 0, 1), hip_yaw), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Spine", Quaternion((1, 0, 0), 0.13) @ Quaternion((0, 0, 1), -hip_yaw * 0.4), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Chest", Quaternion((1, 0, 0), 0.14) @ Quaternion((0, 0, 1), -hip_yaw * 0.9), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "UpperChest", Quaternion((1, 0, 0), 0.11) @ Quaternion((0, 0, 1), -hip_yaw * 1.1), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Neck", Quaternion((1, 0, 0), -0.05), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Head", Quaternion((1, 0, 0), -0.08), frame=frame)

        for side, sign in (("Left", 1.0), ("Right", -1.0)):
            phase = tau + (0.0 if sign > 0 else math.pi)
            # Forward is -Y: foot swings forward (+Y -> -Y) during aerial phase (sin > 0) and pushes back on ground
            stride_y = 0.34 * math.cos(phase)
            lift_z = max(0.0, math.sin(phase)) * 0.21
            u, l, f, t = [side + k for k in ("UpperLeg", "LowerLeg", "Foot", "Toes")]
            foot_goal = rest[f].head_local.copy()
            foot_goal.x += sign * -0.015
            foot_goal.y += stride_y
            foot_goal.z += lift_z
            _two_bone(rig, rest, heads, rotations, shift, u, l, f, foot_goal, Vector((sign * 0.06, -1.0, 0.0)), frame=frame)
            foot_pitch = -0.32 * math.sin(phase) if math.sin(phase) > 0 else -0.14 * math.cos(phase)
            _set_bone(rig, rest, heads, rotations, shift, f, Quaternion((1, 0, 0), foot_pitch), frame=frame)
            _set_bone(rig, rest, heads, rotations, shift, t, Quaternion((1, 0, 0), max(0.0, -foot_pitch * 0.4)), frame=frame)

            # Natural runner arm pump cleanly at sides (bent elbows ~80 deg, zero chest-crossing!)
            arm_phase = phase + math.pi
            swing = -math.cos(arm_phase)  # +1 forward (-Y) when opposite leg drives forward
            _set_bone(rig, rest, heads, rotations, shift, side + "Shoulder", Quaternion((0, 0, 1), -0.08 * swing), frame=frame)
            ua, la, hd = [side + k for k in ("UpperArm", "LowerArm", "Hand")]
            ua_pos = heads[rest[ua].parent.name] + rotations[rest[ua].parent.name] @ (rest[ua].head_local - rest[ua].parent.head_local)
            arm_len = (rest[la].head_local - rest[ua].head_local).length + (rest[hd].head_local - rest[la].head_local).length
            hand_goal = ua_pos + Vector((
                sign * (0.14 - 0.025 * max(0.0, swing)),
                -0.23 * swing - 0.11,
                -arm_len * (0.56 - 0.14 * swing),
            ))
            _two_bone(rig, rest, heads, rotations, shift, ua, la, hd, hand_goal, Vector((sign * 0.30, 0.90, -0.32)), frame=frame)
            hand_rot = Quaternion((0, 1, 0), sign * -0.18) @ rotations[la]
            _set_bone(rig, rest, heads, rotations, shift, hd, hand_rot, frame=frame)
            _pose_fingers(rig, rest, heads, rotations, shift, side, hand_rot, 0.34, frame)

    else:  # AnimePose (Figurine showcase contrapposto)
        breath = 0.5 - 0.5 * c
        shift = Vector((0.028 + 0.004 * s, 0.0, -0.012 + 0.004 * breath))
        _set_bone(rig, rest, heads, rotations, shift, "Hips", Quaternion((0, 1, 0), 0.075) @ Quaternion((0, 0, 1), -0.14), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Spine", Quaternion((0, 1, 0), -0.035) @ Quaternion((0, 0, 1), 0.06), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Chest", Quaternion((1, 0, 0), -0.02 - 0.015 * breath) @ Quaternion((0, 1, 0), -0.045) @ Quaternion((0, 0, 1), 0.10), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "UpperChest", Quaternion((1, 0, 0), -0.015 - 0.010 * breath) @ Quaternion((0, 0, 1), 0.05), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Neck", Quaternion((0, 1, 0), 0.03), frame=frame)
        _set_bone(rig, rest, heads, rotations, shift, "Head", Quaternion((0, 1, 0), 0.075 + 0.012 * s) @ Quaternion((0, 0, 1), 0.06) @ Quaternion((1, 0, 0), 0.025), frame=frame)

        # Right leg grounded, left leg lightly stepped forward & slightly turned out
        for side, sign in (("Left", 1.0), ("Right", -1.0)):
            u, l, f, t = [side + k for k in ("UpperLeg", "LowerLeg", "Foot", "Toes")]
            foot_goal = rest[f].head_local.copy()
            if sign > 0:
                foot_goal.x += 0.055
                foot_goal.y += -0.095
            else:
                foot_goal.x -= 0.010
                foot_goal.y += 0.035
            _two_bone(rig, rest, heads, rotations, shift, u, l, f, foot_goal, Vector((sign * 0.12, -1.0, 0.0)), frame=frame)
            _set_bone(rig, rest, heads, rotations, shift, f, Quaternion((0, 0, 1), sign * 0.16), frame=frame)
            _set_bone(rig, rest, heads, rotations, shift, t, Quaternion((0, 0, 1), sign * 0.16), frame=frame)

            _set_bone(rig, rest, heads, rotations, shift, side + "Shoulder", Quaternion((0, 1, 0), sign * (-0.03 - 0.01 * breath)), frame=frame)
            ua, la, hd = [side + k for k in ("UpperArm", "LowerArm", "Hand")]
            ua_pos = heads[rest[ua].parent.name] + rotations[rest[ua].parent.name] @ (rest[ua].head_local - rest[ua].parent.head_local)
            arm_len = (rest[la].head_local - rest[ua].head_local).length + (rest[hd].head_local - rest[la].head_local).length
            if sign > 0:
                # Left hand poised gracefully slightly forward & outward
                hand_goal = ua_pos + Vector((0.20, -0.14, -arm_len * 0.78))
                hint = Vector((0.65, 0.72, -0.20))
            else:
                # Right hand resting delicately near hip/skirt edge
                hand_goal = ua_pos + Vector((-0.145, -0.05, -arm_len * 0.90))
                hint = Vector((-0.55, 0.80, -0.15))
            _two_bone(rig, rest, heads, rotations, shift, ua, la, hd, hand_goal, hint, frame=frame)
            hand_rot = Quaternion((0, 1, 0), sign * -0.18) @ Quaternion((1, 0, 0), -0.15) @ rotations[la]
            _set_bone(rig, rest, heads, rotations, shift, hd, hand_rot, frame=frame)
            _pose_fingers(rig, rest, heads, rotations, shift, side, hand_rot, 0.14, frame)

    bpy.context.view_layer.update()


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    donor_fbx = Path(args[0])
    out_fbx = Path(args[1])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(donor_fbx), automatic_bone_orientation=False)

    rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
    # Remove mesh objects so the exported animation donor FBX is lightweight (<200KB)
    for obj in list(bpy.context.scene.objects):
        if obj != rig:
            bpy.data.objects.remove(obj, do_unlink=True)
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)

    bpy.context.scene.render.fps = 30
    anim = rig.animation_data_create()
    specs = [
        ("AnimeIdle", 72, 12),
        ("AnimeWalk", 34, 16),
        ("AnimeRun", 22, 16),
        ("AnimePose", 90, 12),
    ]
    for name, length, steps in specs:
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        anim.action = act
        for i in range(steps + 1):
            frac = i / float(steps)
            frame = 1.0 + length * frac
            pose_anime(rig, name, frac, frame)
        # Smooth cubic bezier handles (compatible with Blender 4.x and 5.x layered actions)
        fcurves = getattr(act, "fcurves", None)
        if fcurves is None and hasattr(act, "layers"):
            fcurves = []
            for layer in act.layers:
                for strip in layer.strips:
                    if hasattr(strip, "channelbags"):
                        for bag in strip.channelbags:
                            fcurves.extend(bag.fcurves)
        if fcurves:
            for fcurve in fcurves:
                for kp in fcurve.keyframe_points:
                    kp.interpolation = "BEZIER"
                    kp.handle_left_type = "AUTO_CLAMPED"
                    kp.handle_right_type = "AUTO_CLAMPED"

    anim.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    out_fbx.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(out_fbx),
        use_selection=True,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0.05,
        add_leaf_bones=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
    )
    print(f"[AnimeShowcaseMotions] Exported {len(specs)} graceful anime RPG clips -> {out_fbx}", flush=True)


if __name__ == "__main__":
    main()
