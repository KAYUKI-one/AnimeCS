"""Prepare the local Dust II background world, HDR panorama, and official Source-space radar."""
from pathlib import Path
from PIL import Image
from concurrent.futures import ThreadPoolExecutor
import json,shutil,sys
src,dst=map(Path,sys.argv[1:])
visual=dst/'DustII.gltf'
doc=json.loads(visual.read_text())
def remove_effect_cards(model):
    effects={'steam_001','lightshaft_001','dust_001','dust_001b','dust_001_skybox','nuke_clouds_002','sun_disc_glow_001'}
    for mesh in model['meshes']:
        mesh['primitives']=[p for p in mesh['primitives'] if model['materials'][p['material']]['name'] not in effects]
    remap={i:j for j,i in enumerate(i for i,m in enumerate(model['meshes']) if m['primitives'])}
    for n in model['nodes']:
        if 'mesh' in n:
            original=n.pop('mesh')
            if original in remap:n['mesh']=remap[original]
    model['meshes']=[m for m in model['meshes'] if m['primitives']]
remove_effect_cards(doc)
visual.write_text(json.dumps(doc,separators=(',',':')),encoding='utf8')
out=dst/'Horizon';out.mkdir(exist_ok=True);(out/'Textures').mkdir(exist_ok=True)
sky=src/'DustDistant';d=json.loads((sky/'sky.gltf').read_text())
remove_effect_cards(d)
for node in d['nodes']:
    node.get('extensions',{}).pop('KHR_lights_punctual',None)
    if not node.get('extensions'):node.pop('extensions',None)
d.get('extensions',{}).pop('KHR_lights_punctual',None)
for key in ('extensionsUsed','extensionsRequired'):
    if key in d:d[key]=[e for e in d[key] if e!='KHR_lights_punctual']
for b in d['buffers']:shutil.copyfile(sky/b['uri'],out/b['uri'])
def shrink(row):
    p=sky/row['uri'];relative='Textures/'+p.name
    with Image.open(p) as im:
        im.thumbnail((512,512),Image.Resampling.LANCZOS);im.save(out/relative,compress_level=3)
    row['uri']=relative
with ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(shrink,d['images']))
(out/'Horizon.gltf').write_text(json.dumps(d,separators=(',',':')),encoding='utf8')
shutil.copyfile(src/'DustSky.exr',dst/'Daylight.exr')
shutil.copyfile(src/'DustOverview.png',dst/'Overhead.png')
