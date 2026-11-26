"""Apply a local virtual Case Hardened paint pattern to the existing Ember-47 UV map."""
from pathlib import Path
from PIL import Image
import numpy as np,sys
root=Path(sys.argv[1]);pattern=Path(sys.argv[2])
base=Image.open(root/'Ember47_T00.png').convert('RGB')
base.thumbnail((2048,2048),Image.Resampling.LANCZOS)
b=np.asarray(base,dtype=np.float32)/255
pat=np.asarray(Image.open(pattern).convert('RGB').resize(base.size,Image.Resampling.LANCZOS),dtype=np.float32)/255
# Preserve wood, black polymer and original engraved detail. Cool-coloured metal islands keep their UV shading.
l=b.mean(axis=2);wood=np.clip((b[:,:,0]-b[:,:,2]-.04)*11,0,1)
m=np.clip((l-.14)/.21,0,1)*(1-wood)
tone=.38+.72*np.sqrt(l)
finish=np.clip(pat*tone[:,:,None],0,1);out=b*(1-m[:,:,None])+finish*m[:,:,None]
Image.fromarray(np.uint8(out*255)).save(root/'Ember47Case.png')
