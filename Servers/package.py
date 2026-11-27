"""Create Debian archives with the executable modes preserved by tar."""
from pathlib import Path
import sys
import tarfile

root = Path(sys.argv[1]).resolve()
units = Path(__file__).resolve().parent / "debian"
archives = root / "Packages"
archives.mkdir(exist_ok=True)
roles = sys.argv[2:] or ("Battle", "Data", "HotUpdate")
if any(role not in ("Battle", "Data", "HotUpdate") for role in roles):
    raise SystemExit("Expected a role from Battle, Data or HotUpdate")
for role in roles:
    folder = root / role
    main = f"AnimeCS.{role}" + (".x86_64" if role == "Battle" else "")
    if not (folder / main).is_file():
        raise FileNotFoundError(folder / main)
    name = f"AnimeCS-{role}-debian13-x64.tar.gz"
    stage = archives / (name + ".tmp")
    with tarfile.open(stage, "w:gz", compresslevel=5) as out:
        for file in sorted(folder.rglob("*")):
            if not file.is_file() or file.suffix == ".pdb" or any(p.endswith("_DoNotShip") for p in file.parts):
                continue
            path = role + "/" + file.relative_to(folder).as_posix()
            info = out.gettarinfo(str(file), path)
            info.mode = 0o755 if file.name in (main, "UnityCrashHandler64") else 0o644
            with file.open("rb") as content:
                out.addfile(info, content)
        unit = "animecs-" + role.lower() + ".service"
        unit_path = units / unit
        info = out.gettarinfo(str(unit_path), arcname=role + "/" + unit)
        info.mode = 0o644
        with unit_path.open("rb") as content:
            out.addfile(info, content)
    stage.replace(archives / name)
    print(name)
