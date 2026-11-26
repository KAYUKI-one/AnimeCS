"""Prepare URP albedo and metallic/smoothness textures for local demo inspection."""
from pathlib import Path
import sys,numpy as np
from PIL import Image,ImageDraw,ImageFilter
import json
src,art=map(Path,sys.argv[1:])
for name,s,base,normal,orm in [('Longglass96','awp','awp_default_color*','awp_default_normal*','awp_default_ao*'),('Feather08','ssg08','ssg08_default_color*','ssg08_default_normal*','ssg08_default_ao*'),('RubyEdge','Knife','knife_bayonet_color*','default_normal*','knife_bayonet_ao*')]:
    folder=art/'Weapons'/name;folder.mkdir(exist_ok=True,parents=True)
    def im(glob):return Image.open(next((src/s).glob(glob+'.png'))).convert('RGB')
    a=np.array(im(base),dtype=np.float32)/255
    # Preserve authored tonal differences, stamped markings, glass and dark trim.
    if s=='ssg08':
        doc=json.loads((src/s/(s+'.gltf')).read_text());buf=(src/s/doc['buffers'][0]['uri']).read_bytes()
        def acc(i):
            v=doc['accessors'][i];b=doc['bufferViews'][v['bufferView']];n={'SCALAR':1,'VEC2':2,'VEC3':3}[v['type']]
            fmt={5126:'<f4',5125:'<u4',5123:'<u2'}[v['componentType']];stride=np.dtype(fmt).itemsize
            return np.ndarray((v['count'],n),dtype=fmt,buffer=buf,offset=b.get('byteOffset',0)+v.get('byteOffset',0),strides=(b.get('byteStride',n*stride),stride))
        pr=doc['meshes'][1]['primitives'][0];pos=acc(pr['attributes']['POSITION']);uv=acc(pr['attributes']['TEXCOORD_0']);ix=acc(pr['indices']).reshape(-1,3)
        painting=Image.new('L',(a.shape[1],a.shape[0]));draw=ImageDraw.Draw(painting)
        for face in ix:
            xx,yy,zz=pos[face].mean(axis=0)
            if -.055<zz<.638 and -.037<yy<.090 or .105<zz<.33 and yy>.15:
                poly=uv[face]*[a.shape[1],a.shape[0]];draw.polygon([tuple(pt) for pt in poly],fill=255)
        m=np.array(painting.filter(ImageFilter.MaxFilter(3)),dtype=np.float32)/255
        l=a.mean(axis=2); finish=np.clip((.22+1.33*l)[...,None]*np.array([.115,.34,.48]),0,1)
        a=a*(1-m[...,None])+finish*m[...,None]
    if s=='Knife': a=np.clip(.035+a.mean(axis=2)[...,None]*np.array([.44,.49,.52]),0,1)
    Image.fromarray(np.uint8(np.clip(a,0,1)*255)).save(folder/(name+'_T00.png'));im(normal).save(folder/(name+'_N.png'))
    o=np.array(im(orm));t=np.empty((*o.shape[:2],4),dtype='uint8');t[:,:,:3]=o[:,:,2,None];t[:,:,3]=255-o[:,:,1]
    Image.fromarray(t).save(folder/(name+'_Metal.png'))
    if s=='Knife':
        yy,xx=np.mgrid[:768,:768];r=np.random.default_rng(421)
        cloud=np.zeros((768,768))
        for size,weight in [(5,.5),(21,.28),(61,.12),(180,.08)]:
            cloud+=weight*np.array(Image.fromarray(np.uint8(r.random((size,size))*255)).resize((768,768),Image.Resampling.BICUBIC))/255
        bands=(.5+.5*np.sin((xx*.016+yy*.006+cloud*8)*3.4))**6
        rgb=np.stack((.27+.53*cloud+.13*bands,.022+.049*cloud+.034*bands,.05+.077*cloud+.043*bands),axis=-1)
        Image.fromarray(np.uint8(np.clip(rgb,0,1)*255)).save(folder/(name+'_Ruby.png'))
