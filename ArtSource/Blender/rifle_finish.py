"""Remove photographed reflections from the demo rifle's color atlas and author its steel/wood response."""
from pathlib import Path
import numpy as np
from PIL import Image

folder=Path(__file__).resolve().parents[2]/"Assets/AnimeCS/Art/Weapons/Ember47"
base=np.asarray(Image.open(folder/"Ember47_T00.png").convert("RGB"),dtype=np.float32)/255
r,g,b=base.transpose(2,0,1)
# The original UV atlas photographs both wood and a polished steel rifle under a studio light.
wood=np.clip(((r-b)-.035)*9,0,1)*np.clip(((r-g)-.015)*14,0,1)
luma=base@np.array([.2126,.7152,.0722],dtype=np.float32)
steel_luma=.115+.34*np.power(luma,.87)
steel=(base-luma[:,:,None])*.40+steel_luma[:,:,None]*np.array([.96,.99,1.04],dtype=np.float32)
wood_color=base*(.84+.08*(1-luma[:,:,None]))
albedo=np.clip(steel*(1-wood[:,:,None])+wood_color*wood[:,:,None],0,1)
Image.fromarray(np.rint(albedo*255).astype("u1")).save(folder/"Ember47Albedo.png")
surface=np.zeros((*r.shape,4),dtype="u1")
surface[:,:,0]=np.rint(255*(.74*(1-wood)+.025*wood))
surface[:,:,3]=np.rint(255*(.30+.19*luma+.025*wood))
Image.fromarray(surface).save(folder/"Ember47Surface.png")
