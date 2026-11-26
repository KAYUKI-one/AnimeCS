"""Bake the glove's four authored UV panel masks into an original iris-coloured URP finish."""
import numpy as np,sys
from PIL import Image
from pathlib import Path
src,art=map(Path,sys.argv[1:]);source=src/'Gloves';dest=art/'Wearables'/'Iris'
dest.mkdir(parents=True,exist_ok=True)
size=1536
mask=np.array(Image.open(source/'sport-mask.png').convert('RGB').resize((size,size),Image.Resampling.BILINEAR),dtype='f4')/255
normal=Image.open(next(source.glob('sporty_glove_normal*.png'))).convert('RGB')
normal.resize((size,size),Image.Resampling.LANCZOS).save(dest/'Iris_N.png')
y,x=np.mgrid[:size,:size];grip=.5+.5*np.cos(2*np.pi*x/10)*np.cos(2*np.pi*y/13)
weave=.94+.06*np.sin(np.pi*(x+y)/4)*np.cos(np.pi*(x-y)/6)
pat=np.array(Image.open(source/'pandora.png').convert('RGB').resize((size//3,size//3),Image.Resampling.LANCZOS),dtype='f4')/255
pat=np.tile(pat,(3,3,1));pattern=(pat[:,:,0]*.35+pat[:,:,1]*.54+pat[:,:,2]*.11)
black=np.array([.105,.106,.14]); violet=np.array([.52,.29,.72]); trim=np.array([.60,.63,.73]); pad=np.array([.27,.22,.33])
rgb=np.broadcast_to(black,(size,size,3)).copy()
for i,col in [(2,pad),(0,violet),(1,trim)]:
    a=mask[:,:,i,None];rgb=rgb*(1-a)+col*a
rgb*=weave[:,:,None]*(.83+.17*grip[:,:,None])
rgb*=1+mask[:,:,2,None]*(pattern[:,:,None]-.35)*.72
Image.fromarray(np.uint8(np.clip(rgb,0,1)*255)).save(dest/'Iris_T00.png')
