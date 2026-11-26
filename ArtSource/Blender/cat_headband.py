"""Build the separate cat-ear headband for the headwear slot."""
import bpy,math,sys
from pathlib import Path
root=Path(sys.argv[-1])
def mat(name):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    col=name.split('_')[-1];m.diffuse_color=tuple((int(col[k:k+2],16)/255)**2.2 for k in (0,2,4))+(1,)
    return m
def surface(name,pts,polys,color):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(pts,[],polys);mesh.update()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);mesh.materials.append(mat(color))
    for f in mesh.polygons:f.use_smooth=True
    return o
def cord(name,points,r,color):
    c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=r;c.bevel_resolution=3;c.resolution_u=12
    s=c.splines.new('POLY');s.points.add(len(points)-1)
    for pt,v in zip(s.points,points):pt.co=(*v,1)
    o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);c.materials.append(mat(color));return o
def export(file,objects):
    dg=bpy.context.evaluated_depsgraph_get();items=[]
    for obj in objects:
        m=bpy.data.meshes.new_from_object(obj.evaluated_get(dg),depsgraph=dg);m.transform(obj.matrix_world)
        item=bpy.data.objects.new('Wear',m);bpy.context.collection.objects.link(item);items.append(item)
    bpy.ops.object.select_all(action='DESELECT')
    for item in items:item.select_set(True)
    bpy.context.view_layer.objects.active=items[0];bpy.ops.object.join();bpy.context.object.name=file.stem
    file.parent.mkdir(exist_ok=True,parents=True)
    bpy.ops.export_scene.fbx(filepath=str(file),use_selection=True,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',bake_anim=False)
bpy.ops.wm.read_factory_settings(use_empty=True)
out=root/'Wearables';objs=[]
objs.append(cord('Soft headband',[(.096*math.cos(i*math.pi/48),.005,.031+.125*math.sin(i*math.pi/48)) for i in range(49)],.005,'Band_393445'))
for side in (-1,1):
    a,b,c=(side*.048,-.014,.139),(side*.087,-.006,.116),(side*.087,.001,.190)
    back=[(x,y+.021,z) for x,y,z in (a,b,c)]
    ear=surface('Ear', [a,b,c]+back,[(0,1,2),(5,4,3),(0,3,4,1),(1,4,5,2),(2,5,3,0)],'Band_302B39')
    mod=ear.modifiers.new('Soft edges','BEVEL');mod.width=.003;mod.segments=3;objs.append(ear)
    objs.append(surface('Ear lining',[(side*.059,-.015,.140),(side*.080,-.010,.129),(side*.082,-.004,.173)],[(0,1,2),(2,1,0)],'Lining_BA8798'))
export(out/'CatBand.fbx',objs)
