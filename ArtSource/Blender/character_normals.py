"""Build Unity tangent-space normals from matching authored RG maps. Requires Pillow and NumPy."""
from pathlib import Path
from cast_roster import folders
import hashlib
import sys
import numpy as np
from PIL import Image


def convert(source, target, flip_green):
    rg = np.asarray(Image.open(source).convert("RGB"), dtype=np.float32)[:, :, :2] / 127.5 - 1.
    if flip_green:
        rg[:, :, 1] *= -1.
    z = np.sqrt(np.maximum(0., 1.-np.sum(rg*rg, axis=2)))
    xyz = np.dstack((rg, z))
    xyz /= np.maximum(np.linalg.norm(xyz, axis=2, keepdims=True), 1.e-5)
    Image.fromarray(np.rint((xyz+1.)*127.5).astype(np.uint8)).save(target)


def sync(source, destination):
    # Match the original albedo bytes before deriving the normal name. Eye/face atlases may share dimensions.
    for name, fragment, flip in (("Rover","男漂",True),("Flor","4ce938",True),("Mornye","莫宁",True),("Laevatain","莱万汀",False)):
        folder = next(p for p in source.iterdir() if p.is_dir() and fragment in p.name)
        originals = {hashlib.sha256(p.read_bytes()).digest(): p for p in folder.rglob("*_D.png")}
        normals = {p.stem:p for p in folder.rglob("*_N.png")}
        for diffuse in (destination/folders[name]).glob(name+"_T[0-9][0-9].png"):
            original = originals.get(hashlib.sha256(diffuse.read_bytes()).digest())
            normal = normals.get(original.stem[:-2]+"_N") if original else None
            if normal:
                target = diffuse.with_name(diffuse.stem+"_Normal.png")
                convert(normal, target, flip)
                print(f"{name} / {diffuse.stem} <- {normal.name}", flush=True)


if __name__ == "__main__":
    sync(Path(sys.argv[1]), Path(sys.argv[2]))

