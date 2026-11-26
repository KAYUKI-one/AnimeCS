"""Original sealed game props, in metres. blender -b -P field_kit.py -- OUTPUT_DIR"""
import math, sys
from pathlib import Path
import bpy
from mathutils import Vector

out=Path(sys.argv[sys.argv.index("--")+1]); out.mkdir(parents=True,exist_ok=True)
palette={"Basalt":(.038,.055,.075),"Titanium":(.28,.34,.37),"Ceramic":(.73,.76,.70),"Copper":(.60,.27,.09),
         "Petrol":(.035,.26,.29),"Amber":(.91,.38,.045),"Glacier":(.09,.79,.88),"Violet":(.43,.26,.73),
         "BlackGlass":(.018,.036,.055),"Ivory":(.85,.85,.78)}
def begin():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for key,col in palette.items():
        m=bpy.data.materials.new(key);m.diffuse_color=(*col,1)
def finish(o,mat,bevel=0):
    o.data.materials.append(bpy.data.materials[mat])
    bpy.context.view_layer.objects.active=o
    if bevel:
        b=o.modifiers.new("Rolled edge","BEVEL");b.width=bevel;b.segments=3
        bpy.ops.object.modifier_apply(modifier=b.name)
    n=o.modifiers.new("Panel normals","WEIGHTED_NORMAL");n.keep_sharp=True
    bpy.ops.object.modifier_apply(modifier=n.name)
    return o
def block(name,at,scale,mat,b=.001,angle=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=at);o=bpy.context.object;o.name=name
    o.dimensions=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.rotation_euler.z=angle
    return finish(o,mat,b)
def loft(name,rings,mat,sides=48,aspect=1,phase=0):
    verts=[(r*math.cos(i*math.tau/sides+phase)*aspect,r*math.sin(i*math.tau/sides+phase),z) for z,r in rings for i in range(sides)]
    faces=[tuple(reversed(range(sides)))]
    for j in range(len(rings)-1):
        for i in range(sides):
            a=j*sides+i;c=j*sides+(i+1)%sides;faces.append((a,c,c+sides,a+sides))
    faces.append(tuple(range(len(verts)-sides,len(verts))))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o)
    for p in mesh.polygons[1:-1]:p.use_smooth=sides>=24
    return finish(o,mat)
def panel(name,xy,z,h,mat,b=.002):
    n=len(xy);verts=[(x,y,k) for k in (z,z+h) for x,y in xy]
    faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update()
    o=bpy.data.objects.new(name,m);bpy.context.collection.objects.link(o);return finish(o,mat,b)
def band(name,z,r,h,mat,n=48):
    return loft(name,[(z-h/2,r*.97),(z-h*.30,r),(z+h*.30,r),(z+h/2,r*.97)],mat,n)
def export(name):
    only=sys.argv[sys.argv.index("--")+2:]
    if only and name not in only:return
    # Merge pieces of each finish to keep the prop's draw calls independent of its detail count.
    for mat in tuple(bpy.data.materials):
        group=[o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.data.materials) and o.data.materials[0]==mat]
        if len(group)>1:
            bpy.ops.object.select_all(action='DESELECT')
            for o in group:o.select_set(True)
            bpy.context.view_layer.objects.active=group[0];bpy.ops.object.join()
    bpy.ops.export_scene.fbx(filepath=str(out/(name+".fbx")),axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_anim=False,add_leaf_bones=False,path_mode="STRIP")
begin()
loft("Pressure glaze",[(-.085,.032),(-.080,.044),(-.065,.047),(.045,.048),(.061,.044),(.071,.034)],"Petrol")
for z,r,h,mat in [(-.071,.049,.012,"Basalt"),(-.058,.048,.004,"Titanium"),(.040,.05,.011,"Basalt"),(.05,.049,.004,"Glacier"),(.069,.037,.014,"Titanium")]:band("Collar",z,r,h,mat)
loft("Crown",[(.070,.032),(.081,.028),(.086,.019)],"BlackGlass",12)
for i in range(10):
    a=i*math.tau/10;u=math.cos(a);v=math.sin(a)
    block("Grip flute",(.0465*u,.0465*v,-.015),(.004,.010,.061),"Basalt",.0018,a)
    for j in range(3):block("Vapour dash",(.049*u,.049*v,.001+j*.007),(.003,.010-j*.002,.0025),"Glacier",.0007,a)
export("Smoke")
begin()
loft("Flare shell",[(-.082,.031),(-.073,.041),(-.058,.043),(.054,.043),(.067,.038),(.073,.032)],"Ceramic",8,1,math.pi/8)
for z in (-.065,.058):band("Protective bumper",z,.047,.016,"Basalt",8);band("Burnished lip",z+.010,.043,.004,"Copper",8)
band("Amber crown",.075,.032,.015,"Amber",8)
for i in range(8):
    a=i*math.tau/8;u=math.cos(a);v=math.sin(a)
    block("Inset lens",(.0399*u,.0399*v,0),(.005,.017,.066),"Titanium",.002,a)
    for j in range(4):block("Light cell",(.043*u,.043*v,-.023+j*.015),(.0025,.012,.0085),"Amber",.001,a)
export("Flash")
begin()
loft("Echo carrier",[(-.029,.047),(-.019,.061),(.010,.063),(.024,.053),(.029,.040)],"Basalt",18)
for z,rad,c in ((.014,.063,"Violet"),(.027,.044,"Titanium"),(.031,.030,"Violet")):band("Resonance ring",z,rad,.005,c,32)
for i in range(3):
    a=i*math.tau/3
    block("Signal",(.027*math.cos(a),.027*math.sin(a),.033),(.017,.004,.002),"Ivory",.001,a)
export("Echo")
begin()
outline=[(-.228,-.073),(-.194,-.143),(-.094,-.163),(.137,-.157),(.222,-.108),(.221,.083),(.166,.145),(-.129,.157),(-.218,.109)]
panel("Sealed composite chassis",outline,0,.077,"Basalt",.009)
panel("Titanium shadow seam",[(x*1.006,y*1.006) for x,y in outline],.040,.006,"Titanium")
panel("Top seal",[(x*.972,y*.965) for x,y in outline],.073,.010,"BlackGlass")
for k in (-1,1):
    pts=[(.195,-.094),(.193,.075),(.144,.119),(.066,.118),(.030,.077),(.086,.037),(.100,-.032),(.052,-.093),(.110,-.122)]
    xy=[(k*x,k*y) for x,y in pts]
    panel("Folded ceramic wing",xy,.081,.020,"Ceramic",.004)
    rail=[(.181,-.056),(.174,.063),(.152,.080),(.156,-.041)]
    panel("Copper binding",[(k*x,k*y) for x,y in rail],.101,.003,"Copper",.0009)
    for i in range(5):block("Status ticks",(k*(.150-.010*i),k*(-.092),.1015),(.005,.017,.0025),"Titanium",.0006,-.35)
band("Emitter bezel",.086,.083,.016,"Titanium",12)
band("Luminous gasket",.097,.067,.007,"Glacier",24)
loft("Faceted opal",[(.097,.062),(.113,.055),(.127,.034),(.133,.014)],"Petrol",12,1,.16)
for i in range(3):
    a=math.tau*i/3+.12;u=math.cos(a);v=math.sin(a)
    block("Opal inlay",(.040*u,.040*v,.118),(.035,.005,.0035),"Glacier",.001,a)
for i in range(3):block("Side rib",(-.190+i*.019,-.136,.043),(.009,.016,.046),"Titanium",.003)
export("ObjectiveCase")
begin()
oval=[(-.125,-.046),(-.099,-.070),(.104,-.070),(.126,-.045),(.126,.039),(.100,.067),(-.094,.067),(-.125,.045)]
panel("Field scanner sleeve",oval,.003,.052,"Titanium",.008)
panel("Folding blue cover",[(x*.98,y*.98) for x,y in oval],.053,.012,"Petrol",.004)
for x in (-.083,.083):
    block("Fabric closure",(x,0,.065),(.018,.131,.012),"Basalt",.003)
block("Rescue mark",(0,0,.068),(.042,.009,.002),"Ivory",.0007)
block("Rescue mark",(0,0,.069),(.009,.040,.002),"Ivory",.0007)
export("RecoveryKit")

