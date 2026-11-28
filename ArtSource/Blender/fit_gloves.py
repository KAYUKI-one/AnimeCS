"""Fit gloves from a local GLTF export folder or an existing fitted FBX onto the cast."""
import bpy,bmesh,re,sys
from pathlib import Path
from mathutils import Vector,Matrix
sys.path.insert(0,str(Path(__file__).parent))
from cast_roster import playable, folders
params=sys.argv[sys.argv.index('--')+1:]
source,art=map(Path,params[:2])
out=art/'Wearables'/'Iris';out.mkdir(parents=True,exist_ok=True)
for cast in params[2:] or playable:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    fitted = source.suffix.lower() == ".fbx"
    if fitted:
        bpy.ops.import_scene.fbx(filepath=str(source), use_anim=False, ignore_leaf_bones=False)
    else:
        bpy.ops.import_scene.gltf(filepath=str(source/'Gloves/gloves.gltf'))
    old=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    glove=next(o for o in bpy.context.scene.objects if o.type=='MESH' and
               (o.find_armature()==old if fitted else o.name.endswith('.viewmodel')))
    bpy.ops.import_scene.fbx(filepath=str(art/'Characters'/folders[cast]/(cast+'.fbx')),use_anim=False,ignore_leaf_bones=False)
    rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE' and o!=old)
    def head(ob,n): return ob.matrix_world@ob.data.bones[n].head_local
    def frame(along,across):
        y=along.normalized();x=across-y*across.dot(y)
        if x.length<.00001:x=Vector((0,0,1)).cross(y)
        x.normalize();return Matrix((x,y,x.cross(y))).transposed()
    mapping={};seg={}
    for side,long in [('L','Left'),('R','Right')]:
        def source_bone(gltf, fbx):
            return long+fbx if fitted else gltf+'_'+side
        across=head(old,source_bone('finger_index_0','IndexProximal'))-head(old,source_bone('finger_pinky_0','LittleProximal'))
        across2=head(rig,long+'IndexProximal')-head(rig,long+'LittleProximal')
        breadth=across2.length/across.length
        def add(src,dst,tip,dest_tip,previous=None,last=False):
            key=source_bone(src,dst);dest=long+dst
            h=head(old,key);k=head(rig,dest)
            source_tip=source_bone(previous if last else tip,dest_tip)
            if last:
                axis=h-head(old,source_tip);axis2=k-head(rig,long+dest_tip)
            else:
                axis=head(old,source_tip)-h;axis2=head(rig,long+dest_tip)-k
            local=frame(axis,across);dest_frame=frame(axis2,across2)
            length=axis2.length/axis.length
            scale=Matrix.Diagonal(Vector((breadth*1.07,length,breadth*1.07)))
            mapping[key]=dest
            seg[key]=(h,k,dest_frame@scale@local.transposed())
        add('hand','Hand','finger_middle_0','MiddleProximal')
        add('arm_lower','LowerArm','hand','Hand')
        if not fitted:
            for suffix in ('_TWIST','_TWIST1'):
                key='arm_lower_'+side+suffix;mapping[key]=long+'LowerArm';seg[key]=seg['arm_lower_'+side]
        for f,newf in [('pinky','Little'),('middle','Middle'),('index','Index'),('ring','Ring'),('thumb','Thumb')]:
            for i,ph in enumerate(['Proximal','Intermediate','Distal']):
                key='finger_'+f+'_'+str(i)
                add(key,newf+ph,'finger_'+f+'_'+str(i+1),newf+('Intermediate' if i==0 or i==2 else 'Distal'),'finger_'+f+'_1',last=i==2)
            if not fitted:
                meta='finger_'+f+'_meta_'+side;mapping[meta]=long+'Hand';seg[meta]=seg['hand_'+side]
    # Remove the original character's bare arms. Only the accessory is transferred.
    if not fitted:
        bm=bmesh.new();bm.from_mesh(glove.data)
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.material_index>=2],context='FACES')
        bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
        bm.to_mesh(glove.data);bm.free()
    source_names=[g.name for g in glove.vertex_groups];weights=[]
    for vertex in glove.data.vertices:
        original=glove.matrix_world@vertex.co;result=Vector((0,0,0));total=0;groups={}
        for w in vertex.groups:
            key=source_names[w.group]
            if w.weight<=0 or key not in seg:continue
            h,k,transform=seg[key];result+=(k+transform@(original-h))*w.weight;total+=w.weight
            groups[mapping[key]]=groups.get(mapping[key],0)+w.weight
        if total<.00001:raise ValueError('Unbound glove vertex '+str(vertex.index))
        vertex.co=result/total;weights.append({n:w/total for n,w in groups.items()})
    glove.parent=None;glove.matrix_world=Matrix.Identity(4);glove.vertex_groups.clear()
    groups={name:glove.vertex_groups.new(name=name) for name in set(k for row in weights for k in row)}
    for i,row in enumerate(weights):
        for n,w in row.items():groups[n].add([i],w,'REPLACE')
    glove.modifiers.clear();mod=glove.modifiers.new('Character hands','ARMATURE');mod.object=rig
    glove.parent=rig;glove.matrix_parent_inverse=rig.matrix_world.inverted()
    glove.data.materials.clear();mat=bpy.data.materials.new('Iris_T00');glove.data.materials.append(mat)
    for face in glove.data.polygons:face.material_index=0;face.use_smooth=True
    glove.name=cast+' fitted gloves'
    bpy.ops.object.select_all(action='DESELECT');glove.select_set(True);rig.select_set(True);bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(out/(cast+'Gloves.fbx')),use_selection=True,axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_ALL',bake_anim=False,add_leaf_bones=False,path_mode='STRIP')
    print('GLOVES',cast,len(weights),flush=True)
