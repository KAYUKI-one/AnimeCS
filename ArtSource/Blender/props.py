"""Game-view meshes and editable Blender art. Usage: blender -b --python props.py -- SOURCES OUTPUT EDITABLE"""
import math
import shutil
import sys
from pathlib import Path
import bpy
import bmesh
from mathutils import Matrix, Vector

source, output, editable = map(Path,sys.argv[sys.argv.index("--")+1:])
editable.mkdir(parents=True,exist_ok=True)
colors={}
def material(color):
    if color not in colors:
        m=bpy.data.materials.new(label+"_"+color)
        m.diffuse_color=tuple((int(color[i:i+2],16)/255)**2.2 for i in (0,2,4))+(1,)
        colors[color]=m
    return colors[color]
def shape(name,outline,width,color,rounding=.36,offset=0):
    count=len(outline)
    vertices=[(x,y+offset,z) for y in (-width/2,width/2) for x,z in outline]
    faces=[tuple(range(count)),tuple(reversed(range(count,2*count)))]
    faces += [(i,i+count,(i+1)%count+count,(i+1)%count) for i in range(count)]
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    bm=bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free()
    obj=bpy.data.objects.new(name,mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(material(color))
    bevel=obj.modifiers.new("Soft edge highlights","BEVEL")
    bevel.width=rounding; bevel.segments=3
    normal=obj.modifiers.new("Flat panels","WEIGHTED_NORMAL")
    normal.keep_sharp=True
    return obj
def strip(name,x1,z1,x2,z2,width,thick,color):
    d=Vector((x2-x1,z2-z1)).normalized()
    x,z=-d.y*thick/2,d.x*thick/2
    return shape(name,[(x1-x,z1-z),(x2-x,z2-z),(x2+x,z2+z),(x1+x,z1+z)],width,color)
def tube(name,rings,z,color):
    verts=[]; faces=[]; count=24
    for x,r in rings:
        for i in range(count):
            a=math.tau*i/count
            verts.append((x,r*math.sin(a),z+r*math.cos(a)))
    for j in range(len(rings)-1):
        for i in range(count):
            a=j*count+i; b=j*count+(i+1)%count
            faces.append((a,b,b+count,a+count))
    faces.extend([tuple(reversed(range(count))),tuple(range(len(verts)-count,len(verts)))])
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(mesh); bm.free()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj); mesh.materials.append(material(color))
    for p in mesh.polygons[:-2]: p.use_smooth=True
def screw(x,z,y,color):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=1,location=(x,y,z))
    obj=bpy.context.object; obj.name="Side fastener"; obj.scale=(.62,.20,.62)
    obj.data.materials.append(material(color))
def optic(start,z,tint,glass):
    tube("Optical silhouette",[(start,2.5),(start+1,2.8),(start+5.5,2.8),(start+7.4,1.6),(start+18,1.6),(start+23,3.0),(start+26,3.0)],z,tint)
    tube("Front glass",[(start+26.01,2.24),(start+26.09,2.18)],z,glass)
    tube("Eye glass",[(start-.08,2.05),(start+.01,2.05)],z,glass)
    for x in (start+8,start+16):
        shape("Optic foot",[(x-1,z-5.5),(x+1.5,z-5.5),(x+1.0,z-1.0),(x-.8,z-1.0)],3.7,"252B30")
        tube("Optic band",[(x-.7,1.94),(x+.7,1.94)],z,"292F35")
    shape("Adjustment cap",[(start+11,z+1),(start+14.5,z+1),(start+14.5,z+3.5),(start+11,z+3.5)],3.45,tint,.35)
def oval(obj, name, outline):
    # The silhouette hole is part of the pictured exterior. Keep its editable cutter in the source only.
    cutter=shape(name,outline,12,"353B3A",.01)
    cutter.modifiers.clear()
    cutter.hide_render=True; cutter.display_type="WIRE"
    mod=obj.modifiers.new(name,"BOOLEAN"); mod.object=cutter; mod.operation="DIFFERENCE"
    obj.modifiers.move(len(obj.modifiers)-1,0)
    cutter.hide_set(True)
def feather():
    blue,plate,black="204C70","2D658B","25272E"
    def pts(points): return [((x-52)/12.27,(490-z)/12.27) for x,z in points]
    def panel(n,p,w,c,r=.22): return shape(n,pts(p),w,c,r)
    panel("Blue chassis",[(330,333),(392,329),(481,302),(998,300),(1004,311),(1001,347),(728,350),(566,361),(333,362)],4.7,blue)
    panel("Faceted lower blue panel",[(511,360),(541,349),(567,313),(717,313),(736,349),(678,406),(578,406),(565,401),(508,401)],4.95,plate)
    panel("Raised black rail",[(482,305),(483,287),(515,276),(723,277),(731,283),(762,283),(762,303)],3.5,black)
    panel("Squarish rear pad",[(52,311),(64,305),(173,305),(185,420),(172,432),(67,435),(58,422)],4.9,"212329",.45)
    panel("Adjustable heel",[(180,295),(214,293),(224,393),(187,397)],4.3,"323238")
    panel("Cheek cushion",[(191,279),(335,279),(344,287),(344,307),(332,312),(230,311),(228,299),(194,299)],4.35,"24262C",.50)
    panel("Stock crosspiece",[(219,331),(343,331),(342,376),(222,379)],3.05,black)
    strip("Stock stay",11,5,23,10,1.4,.52,"323138")
    strip("Rear foot",8.6,4.7,8.8,.8,1.4,.85,black)
    panel("Rear foot pad",[(145,475),(176,474),(181,485),(169,491),(144,490)],2.1,black)
    panel("Slanted grip",[(390,359),(439,359),(436,405),(420,423),(389,475),(348,455),(344,438),(390,386)],3.8,"24252A",.45)
    panel("Trigger surround",[(436,385),(452,391),(517,389),(517,397),(449,399),(434,393)],1.1,plate,.1)
    panel("Magazine inset",[(513,362),(552,362),(548,396),(510,396)],4.6,blue)
    tube("Slender forward silhouette",[(53,1.15),(91.8,1.15),(92.5,1.4),(95.4,1.4),(96,1.14),(96.6,1.4),(100,1.4)],16.25,black)
    strip("Folded front detail",77.5,11.5,88.1,10.9,1.1,.85,black)
    panel("Front detail foot",[(1095,346),(1121,345),(1138,353),(1135,366),(1121,367),(1116,359),(1095,357)],2.05,black,.15)
    optic(31.6,20.8,"234D72","57809B")
    for x,z in ((11.6,11.3),(22.6,11.1),(37.6,12.0),(70,13.4)):
        for side in (-1,1): screw(x,z,side*2.15,"29333B")
def longglass():
    green,dark,steel="646B53","49503F","373C40"
    def pts(points): return [((1358-x)/13.18,(520-y)/13.18) for x,y in points]
    def panel(n,p,w,c,r=.22): return shape(n,pts(p),w,c,r)
    rear=panel("Continuous olive stock",[(1291,383),(1100,384),(980,384),(976,375),(516,374),(503,379),(497,387),(497,413),(504,418),(717,418),(742,426),(775,436),(806,442),(842,443),(855,436),(868,436),(879,441),(886,448),(914,450),(921,426),(931,420),(970,419),(986,427),(997,513),(1002,521),(1039,520),(1063,510),(1109,503),(1130,492),(1146,471),(1219,471),(1227,478),(1224,497),(1232,506),(1288,506)],5.5,green,.49)
    hole=[(1060+34*math.cos(i*math.tau/40),445+27*math.sin(i*math.tau/40)+5*math.cos(i*math.tau/40)) for i in range(40)]
    oval(rear,"Oval thumbhole",pts(hole))
    panel("Recoil pad",[(1282,373),(1329,368),(1342,371),(1351,380),(1357,404),(1357,503),(1351,517),(1289,516),(1294,473),(1294,413)],5.9,"303336",.58)
    panel("Raised cheek piece",[(1102,381),(1106,369),(1117,364),(1142,362),(1256,362),(1274,367),(1287,380)],4.4,"34383A",.40)
    panel("Dark action silhouette",[(725,373),(735,354),(779,352),(787,344),(924,344),(941,353),(979,355),(980,387),(943,387),(938,375)],3.7,steel,.33)
    panel("Lower dark box",[(810,438),(847,439),(861,436),(885,445),(893,451),(891,483),(811,472)],3.55,steel,.25)
    panel("Curved guard",[(914,425),(917,449),(924,456),(939,461),(959,459),(973,449),(983,427),(988,432),(981,452),(965,464),(951,468),(932,467),(918,461),(911,453)],2,dark,.18)
    tube("Metal barrel",[(45,1.30),(56,1.30),(93.4,.82),(95.2,.85),(95.8,1.15),(97.4,1.15),(97.8,.92),(99.4,.92),(100,.70)],11.4,steel)
    strip("Folded support",59.6,6.6,78.4,6.65,1.0,.73,steel)
    strip("Folded brace",57.9,7.8,67,6.5,1,.64,"677071")
    optic(25.1,16.0,dark,"4D6564")
    for x,y in ((544,404),(659,404),(780,413),(908,410),(1034,400),(1155,399),(1206,400),(1223,400),(1265,459),(1026,496)):
        a,b=pts([(x,y)])[0]
        for side in (-1,1): screw(a,b,side*2.72,"40463F")
def bake(obj):
    dg=bpy.context.evaluated_depsgraph_get()
    result=bpy.data.meshes.new_from_object(obj.evaluated_get(dg),depsgraph=dg)
    item=bpy.data.objects.new("Display "+obj.name,result)
    bpy.context.collection.objects.link(item)
    result.transform(obj.matrix_world)
    return item
def fbx(folder, items,fix):
    for o in items: o.data.transform(fix)
    bpy.ops.object.select_all(action="DESELECT")
    for o in items: o.select_set(True)
    bpy.context.view_layer.objects.active=items[0]
    bpy.ops.object.join()
    bpy.context.object.name=label
    bpy.ops.export_scene.fbx(filepath=str(folder/(label+".fbx")),use_selection=True,axis_forward="-Z",axis_up="Y",
                             apply_scale_options="FBX_SCALE_ALL",path_mode="STRIP",bake_anim=False,add_leaf_bones=False)
def tex(image,folder):
    im=folder/(label+"_T00.png")
    if image.packed_file and bytes(image.packed_file.data[:8])==b"\x89PNG\r\n\x1a\n":
        im.write_bytes(image.packed_file.data)
    else:
        image.filepath_raw=str(im);image.file_format="PNG";image.save()
    return bpy.data.materials.new(label+"_T00")
for label, token, ext, size in (("Feather08","","",.99),("Longglass96","","",1.08),("Heavy50","沙漠",".blend",.29),
                               ("Ember47","AK47",".fbx",.86),("Watch41S","m4a1",".obj",.88),("Fold44","m4a4",".blend",.86)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    colors.clear()
    folder=output/"Weapons"/label
    folder.mkdir(parents=True,exist_ok=True)
    if not token:
        if label=="Feather08": feather()
        else: longglass()
        # Keep editable contour pieces, material slots and bevels in the two native source files.
        bpy.context.scene.unit_settings.system="NONE"
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(editable/(label+".blend")),compress=True)
        meshes=[o for o in bpy.context.scene.objects if o.type=="MESH" and not o.hide_render]
    else:
        f=next(next(d for d in source.iterdir() if d.is_dir() and token in d.name).rglob("*"+ext))
        if ext==".blend": bpy.ops.wm.open_mainfile(filepath=str(f),load_ui=False,use_scripts=False)
        if ext==".fbx": bpy.ops.import_scene.fbx(filepath=str(f))
        if ext==".obj": bpy.ops.wm.obj_import(filepath=str(f))
        meshes=[o for o in bpy.context.scene.objects if o.type=="MESH"]
        if label=="Heavy50":
            meshes=[o for o in meshes if o.name in ("枪身","握把","滑膛","枪管","枪管准星","滑膛准星","下导轨","扳机","弹匣","滑膛保险")]
            for obj in meshes:
                for mod in obj.modifiers:
                    if mod.type=="SUBSURF": mod.levels=min(1,mod.levels)
                    if mod.type=="BEVEL": mod.segments=min(3,mod.segments)
        if label=="Fold44": meshes=[o for o in meshes if o.name.endswith("body_legacy")]
    items=[bake(o) for o in meshes if len(o.data.vertices)]
    if token:
        cover=None
        if label=="Ember47":
            shutil.copyfile(next(f.parent.glob("*_texture.png")),folder/(label+"_T00.png"))
            cover=bpy.data.materials.new(label+"_T00")
        if label=="Fold44":
            image=next((i for i in bpy.data.images if "temukau" in i.name and i.size[0]),None)
            if image is None and (folder/(label+"_T00.png")).is_file():
                cover=bpy.data.materials.new(label+"_T00")
            else:
                if image is None: image=next(i for i in bpy.data.images if "default_color" in i.name and i.size[0])
                cover=tex(image,folder)
        for o in items:
            m=cover or material(("555C61" if any(w in o.name for w in ("滑膛","枪管","枪身","机匣","护木")) else "343B40"))
            o.data.materials.clear(); o.data.materials.append(m)
            for p in o.data.polygons: p.material_index=0
    # Source props have inconsistent authoring axes. Identify the narrow/front end from the silhouette.
    longest=1 if label=="Fold44" else 0
    v=[pt.co for o in items for pt in o.data.vertices]
    low=min(p[longest] for p in v); high=max(p[longest] for p in v); span=high-low
    def thickness(arr): return max(p.z for p in arr)-min(p.z for p in arr)
    first=thickness([p for p in v if p[longest]<low+.09*span])
    last=thickness([p for p in v if p[longest]>high-.09*span])
    sign=1 if last<first else -1
    if label in ("Heavy50","Watch41S","Feather08","Longglass96"): sign=1
    angle=(-math.pi/2 if longest==0 else math.pi) + (math.pi if sign==-1 else 0)
    gripfrac=.20 if label=="Heavy50" else .30
    along=(low+gripfrac*span if sign==1 else high-gripfrac*span)
    loz,hiz=min(p.z for p in v),max(p.z for p in v)
    center=Vector((0,0,loz+.36*(hiz-loz)));center[longest]=along
    fix=Matrix.Scale(size/span,4) @ Matrix.Rotation(angle,4,"Z") @ Matrix.Translation(-center)
    print("PROP",label,len(items),sum(len(o.data.vertices) for o in items),sign,flush=True)
    fbx(folder,items,fix)
