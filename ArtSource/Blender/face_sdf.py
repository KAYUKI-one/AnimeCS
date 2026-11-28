"""Bake the Face shader's directional threshold map and a separate facial UV channel."""
import math

import bpy


def bake_face_sdf(body, folder, label):
    mesh = body.data
    face_ids = {vertex for polygon in mesh.polygons
                if mesh.materials[polygon.material_index].name in ("顏", "顔", "颜")
                for vertex in polygon.vertices}
    if not face_ids:
        raise ValueError(f"{label} needs a face surface for the light map.")
    face = [mesh.vertices[i].co for i in face_ids]
    width = max(abs(point.x) for point in face) * 2.18
    chin = min(point.z for point in face)
    height = (max(point.z for point in face) - chin) * 1.12
    # Preserve UV0 and UV1 from the source. The projected SDF basis occupies UV3.
    while len(mesh.uv_layers) < 3:
        mesh.uv_layers.new(name="AnimeCS_FaceSDF" if len(mesh.uv_layers) == 2 else "UV1")
    uv = mesh.uv_layers[2].data
    for loop in mesh.loops:
        point = mesh.vertices[loop.vertex_index].co
        uv[loop.index].uv = (.5 - point.x / width, .05 + (point.z - chin) / height)

    size = 192
    pixels = []
    for row in range(size):
        v = (row + .5) / size
        for column in range(size):
            x = 2.0 * (column + .5) / size - 1.0
            nose = math.exp(-((v - .43) / .155) ** 2 - ((x - .16) / .30) ** 2)
            threshold = .53 + .43 * x + .10 * nose - .14 * (v - .52) ** 2
            value = min(.98, max(.04, threshold))
            pixels.extend((value, 0., 0., 1.))
    image = bpy.data.images.new(label + "FaceSDF", width=size, height=size, alpha=False)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(pixels)
    path = (folder / (label + "FaceSDF.png")).resolve()
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)
    if not path.is_file():
        raise OSError(f"Face SDF was not written to {path}")
