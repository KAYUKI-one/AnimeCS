"""Character folders used by the Unity importer and Blender accessory bakers."""
import json
from pathlib import Path

source = Path(__file__).resolve().parents[2] / "Assets" / "Editor" / "CastRoster.json"
members = json.loads(source.read_text(encoding="utf-8"))["members"]
folders = {member["name"]: member["folder"] for member in members}
playable = tuple(member["name"] for member in members if member["playOrder"] >= 0)
