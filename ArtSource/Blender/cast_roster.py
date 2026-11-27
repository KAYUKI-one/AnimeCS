"""Character folders used by the Unity importer and Blender accessory bakers."""
import json
from pathlib import Path

source = Path(__file__).resolve().parents[2] / "Assets" / "Editor" / "CastRoster.json"
members = json.loads(source.read_text(encoding="utf-8"))["members"]
folders = {member["name"]: member["folder"] for member in members}
playable = tuple(member["name"] for member in members if member["playOrder"] >= 0)

# Offline fitting data for the two tailored source costumes.
# fit = blend interval, waist height, X/Y narrowing, hem lift factor and reference height.
garment_tailoring = {
    "BlackMiku": {
        "fit": (.015, .58, 1.05, .32, .43, .52, 1.02),
        "materials": {"裙G", "裙3", "裙2", "裙1", "裙+", "裙++"},
        "remove": {"头纱"},
    },
    "Flor": {
        "fit": (.01, .53, .98, .20, .33, .48, .98),
        "materials": {"Up_Dress", "Up_Dressin", "Down_Dressin", "Up_Belt", "Down_Belt", "Down_Sliver", "Down_Bag"},
    },
}


def smooth(low, high, value):
    t = max(0., min(1., (value - low) / (high - low)))
    return t * t * (3. - 2. * t)


def fit_garment_point(point, fit):
    if fit is None:
        return point
    low, high, waist, narrow_x, narrow_y, lift, lift_height = fit
    hem = smooth(low, high, waist - point.z)
    point.x *= 1 - narrow_x * hem
    point.y *= 1 - narrow_y * hem
    point.z += lift * max(0, lift_height - point.z)
    return point
