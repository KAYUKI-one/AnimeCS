"""Export a supplied PMX body as a Unity-ready FBX with its original skin weights and bone names.

blender -b --python export_humanoid.py -- model.pmx model.fbx
"""

import argparse
import sys
from pathlib import Path

import bpy


def export_model(source, output):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module="bl_ext.blender_org.mmd_tools")
    bpy.ops.mmd_tools.import_model(filepath=str(source), types={"MESH", "ARMATURE"},
                                 scale=.08, log_level="WARNING")
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH" and obj.find_armature()]
    armatures = {mesh.find_armature() for mesh in meshes}
    if not meshes or len(armatures) != 1:
        raise ValueError("Expected one humanoid armature with one or more skinned meshes.")
    bpy.ops.object.select_all(action="DESELECT")
    armature = next(iter(armatures))
    for obj in [armature, *meshes]:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = armature
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(output), use_selection=True, add_leaf_bones=False,
        bake_anim=False, use_mesh_modifiers=False, path_mode="COPY", embed_textures=True,
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    )
    if not output.is_file():
        raise RuntimeError(f"Blender did not create {output}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.source.suffix.lower() != ".pmx" or not args.source.is_file():
        parser.error("source must be an existing PMX file")
    if args.output.suffix.lower() != ".fbx":
        parser.error("output must be an FBX file")
    export_model(args.source.resolve(), args.output.resolve())
