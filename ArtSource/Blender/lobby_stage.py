"""Editable broadcast concourse set. Input coordinates use Unity Y-up, with the camera on positive Z."""
import bpy, math, sys
from pathlib import Path
from mathutils import Vector

bpy.ops.wm.read_factory_settings(use_empty=True)
out=Path(sys.argv[sys.argv.index("--")+1]);out.parent.mkdir(parents=True,exist_ok=True)
mats={}
def mat(name):
    if name not in mats:
        m=bpy.data.materials.new(name);mats[name]=m
        col=name.rsplit("_",1)[-1];m.diffuse_color=tuple((int(col[i:i+2],16)/255)**2.2 for i in (0,2,4))+(1,)
    return mats[name]
def pt(v):return Vector((-v[0],-v[2],v[1]))
def box(name,x,y,z,w,h,d,c,bevel=.025):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pt((x,y,z)))
    o=bpy.context.object;o.name=name;o.dimensions=(w,d,h)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);o.data.materials.append(mat(c))
    if bevel:
        mod=o.modifiers.new("Chamfer","BEVEL");mod.width=bevel;mod.segments=3
        mod=o.modifiers.new("Panel normals","WEIGHTED_NORMAL")
    return o
def beam(a,b,r,c,name="Steel tube"):
    av,bv=pt(a),pt(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=(av-bv).length,location=(av+bv)*.5)
    o=bpy.context.object;o.name=name;o.rotation_euler=(bv-av).to_track_quat("Z","Y").to_euler()
    o.data.materials.append(mat(c))
floor="Stage_495257"; dark="Stage_202E32"; alloy="Metal_58686A"; mid="Stage_3B494B"
wall="Stage_526264"; glass="Stage_566E75"; brass="Metal_BA9561"; black="Stage_293136"; amber="Glow_F0C27C"; teal="Glow_78BAB6"
box("Foundation",0,-.18,-1,25,.24,22,dark)
for x in range(-5,6):
    for z in range(-4,3):
        box("Floor plate",x*1.62,-.045,z*1.8,1.61,.08,1.786,floor,.013)
for x in (-3.12,3.12):
    box("Floor inset",x,.002,-1,.027,.012,11,brass,.003)
for z in (-2.15,-2.0):
    box("Platform lane",0,.004,z,6.25,.012,.025,brass,.003)
box("Rear concrete wall",0,2.85,-5.2,25,5.7,.45,wall,.08)
box("Raised rear service deck",0,.23,-4.25,20,.46,1.75,mid)
box("Deck edge light",0,.45,-3.37,16,.055,.045,amber,.015)
for x in range(-5,6):
    xx=x*1.59
    box("Window surround",xx,3.2,-4.93,1.52,3.35,.22,dark)
    box("Glazed panel",xx,3.32,-4.78,1.32,2.86,.035,glass)
    for y in (2.18,3.28,4.38):
        box("Window horizontal",xx,y,-4.73,1.44,.045,.065,alloy,.006)
    for off in (-.66,.66):
        box("Window jamb",xx+off,3.32,-4.72,.04,2.9,.09,alloy,.01)
    box("Reflector strip",xx,4.57,-4.70,1.27,.105,.08,teal)
for x in (-6.4,-3.8,3.8,6.4):
    box("Pillar",x,2.5,-4.43,.24,5,.41,mid,.07)
    box("Pillar foot",x,.88,-4.37,.39,.79,.52,alloy)
for side in (-1,1):
    x=side*7
    box("Side wall",x,2.3,-1,.45,4.6,9,mid)
    for z in range(-4,3):box("Acoustic rib",x-side*.28,2.2,z,.26,4.1,.15,dark)
for y in (4.55,4.88):
    beam((-8,y,-3.0),(8,y,-3.0),.046,alloy)
for x in range(-8,8):
    beam((x,4.55,-3),(x+.5,4.88,-3),.025,alloy);beam((x+.5,4.88,-3),(x+1,4.55,-3),.025,alloy)
for x in (-5.8,-2.7,2.7,5.8):
    box("Suspended luminaire",x,4.28,-3.05,1.1,.18,.32,dark)
    box("Warm diffuser",x,4.19,-2.99,1.03,.042,.30,amber)
def roadcase(x,y,z,w,h,d):
    box("Road case",x,y+h/2,z,w,h,d,black,.06)
    for yy in (.08,h-.08):
        box("Flightcase band",x,y+yy,z,w+.016,.045,d+.016,alloy,.014)
    for sx in (-1,1):
        for sz in (-1,1):
            box("Corner protector",x+sx*(w/2-.035),y+h/2,z+sz*(d/2-.035),.073,h,.073,alloy,.018)
    for xx in (-.28,.28):
        box("Case latch",x+xx*w,y+.48*h,z+d/2+.015,.077,.125,.025,brass,.012)
    box("Recessed handle",x,y+.51*h,z+d/2+.02,.22,.072,.03,alloy,.015)
for x,z,w,h,d in [(-3.25,-2.65,1.6,.87,.86),(-4.13,-2.20,.93,.64,.68),(2.65,-2.9,1.42,.73,.8),(3.68,-2.3,.83,.86,.8)]:
    roadcase(x,.0,z,w,h,d)
roadcase(2.83,.73,-2.96,.80,.46,.62)
# A recessed, lit service entrance and a stack of flight cases give the rear deck a readable depth.
box("Service door frame",1.9,1.53,-4.62,1.54,2.14,.20,dark,.045)
for side in (-1,1):
    xx=1.9+side*.335
    box("Sliding service door",xx,1.53,-4.49,.65,2.0,.10,alloy,.025)
    box("Door inset glass",xx,1.87,-4.42,.43,.61,.045,glass,.035)
    box("Safety stripe",xx,1.22,-4.425,.54,.025,.04,brass,.006)
box("Door status light",1.90,2.64,-4.50,1.42,.045,.10,amber)
for x in (-2.9,-2.13):
    box("Equipment cabinet",x,1.36,-4.25,.70,1.8,.50,black,.045)
    for j in range(11):
        box("Vent slot",x,1.04+.092*j,-3.99,.55,.026,.032,alloy,.006)
    box("Tally",x+.23,2.12,-3.98,.12,.032,.03,teal)
for x in (-5.9,4.7):
    for j in range(3):
        beam((x,.57+j*.105,-3.78),(x+2.0,.57+j*.105,-3.78),.036,alloy)
# All furniture is static display geometry. Join by material for an inexpensive set.
objects=list(o for o in bpy.context.scene.objects if o.type=="MESH")
dg=bpy.context.evaluated_depsgraph_get(); baked=[]
for o in objects:
    m=bpy.data.meshes.new_from_object(o.evaluated_get(dg),depsgraph=dg)
    m.transform(o.matrix_world); item=bpy.data.objects.new("Set",m);bpy.context.collection.objects.link(item);baked.append(item)
bpy.ops.object.select_all(action="DESELECT")
for o in baked:o.select_set(True)
bpy.context.view_layer.objects.active=baked[0];bpy.ops.object.join();bpy.context.object.name="Broadcast Concourse"
bpy.ops.export_scene.fbx(filepath=str(out),use_selection=True,axis_forward="-Z",axis_up="Y",apply_scale_options="FBX_SCALE_ALL",bake_anim=False)
