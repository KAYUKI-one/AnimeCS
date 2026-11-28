"""In-place, foot-targeted source actions for the shared Humanoid controller."""
import math
import bpy
from mathutils import Matrix, Quaternion, Vector

# World-space foot spread, stagger, idle hip dip and lateral weight shift in metres.
garment_stances = {"Cantarella": (.012,.023,-.022,.010), "Lily": (.032,.034,-.033,-.012),
                   "Flor": (.024,.038,-.027,.014), "BlackMiku": (.026,.030,-.026,-.008),
                   "Mornye": (.020,.032,-.027,.010), "Laevatain": (.040,.040,-.038,-.009),
                   "StarTraveler": (0.,0.,-.073,0.), "Firefly": (0.,0.,-.073,0.)}

def pose(rig, action, fraction, frame=None, garment=None):
    rest = rig.data.bones
    heads, rotations = {}, {}
    swing = math.sin(fraction*2*math.pi)
    stride = action.startswith(("Run","Walk","Duck")) and not action.endswith("Idle")
    crouch = action.startswith("Duck")
    jump = action in ("Rise","Fall","AirLeft","AirRight")
    direction = (1,0) if action.endswith("Left") else (-1,0) if action.endswith("Right") else (0,1) if action.endswith("Back") else (0,-1)
    length = .28 if action.startswith("Run") else .15 if not crouch else .13
    dip = -.47 if crouch else -.105 if action=="Land" else -.073
    stance=garment_stances.get(garment) if action in ("Idle","DuckIdle") else None
    if stance and not crouch: dip=stance[2]
    if stride: dip -= (.033 if not crouch else .013)*abs(swing)
    if action=="Idle": dip += .006*math.sin(fraction*2*math.pi)
    if action=="View": dip = 0
    shift = Vector((.022*swing if stride else 0, .055 if crouch else 0, dip))
    if stance:
        shift.x+=stance[3]*(.85+.15*math.cos(fraction*2*math.pi))
    def head(name):
        b = rest[name]
        if b.parent is None: return b.head_local+shift
        return heads[b.parent.name]+rotations[b.parent.name]@(b.head_local-b.parent.head_local)
    def set_bone(name, world=None, target=None, tip=None):
        if name not in rest: return
        b = rest[name]
        p = rig.pose.bones[name]
        parent = rotations[b.parent.name] if b.parent else Quaternion()
        original = b.matrix_local.to_quaternion()
        h = head(name)
        if target is not None:
            ray = (rest[tip].head_local if tip else b.tail_local) - b.head_local
            world = ray.rotation_difference(target-h)
        if world is None: world = parent
        local = original.inverted() @ parent.inverted() @ world @ original
        if p.rotation_quaternion.dot(local)<0: local.negate()
        p.rotation_mode="QUATERNION"
        p.rotation_quaternion=local
        if b.parent is None: p.location=original.inverted()@shift
        heads[name],rotations[name]=h,world
        if frame is not None:
            p.keyframe_insert(data_path="rotation_quaternion",frame=frame,group=name)
            if not b.parent: p.keyframe_insert(data_path="location",frame=frame,group=name)
    def two_bone(a,b,c,goal,hint):
        start=head(a)
        line=goal-start
        axis=line.normalized()
        la=(rest[b].head_local-rest[a].head_local).length
        lb=(rest[c].head_local-rest[b].head_local).length
        d=min(la+lb-.001,max(abs(la-lb)+.001,line.length))
        along=(la*la-lb*lb+d*d)/(2*d)
        bend=(hint-axis*hint.dot(axis)).normalized()
        elbow=start+axis*along+bend*math.sqrt(max(0,la*la-along*along))
        set_bone(a,target=elbow,tip=b)
        set_bone(b,target=start+axis*d,tip=c)
    if action in ("View","SidearmView"):
        for name in ("Hips","Spine","Chest","UpperChest","Neck","Head"): set_bone(name)
        for side in ("Left","Right"):
            left = side=="Left"
            set_bone(side+"Shoulder")
            a,b,c=[side+n for n in ("UpperArm","LowerArm","Hand")]
            set_bone(a,target=head(a)+Vector((-.32 if left else .25,-.67,.78)),tip=b)
            set_bone(b,target=head(b)+Vector((-.48 if left else .25,-.69,.71)),tip=c)
            # Palm and knuckle axes, from the character's actual bind pose.
            fingers=[rest[side+f+"Proximal"].head_local for f in ("Index","Middle","Little")]
            def palm_axes(along,across):
                u=along.normalized(); w=u.cross(across).normalized()
                return Matrix((u,w.cross(u),w)).transposed().to_quaternion()
            old=palm_axes(fingers[1]-rest[c].head_local, fingers[0]-fingers[2])
            aim=Vector((-.80,-.25,.48) if left else (.78,-.32,-.24))
            across=Vector((0,-1,0) if left else (0,0,1))
            rot=palm_axes(aim,across) @ old.inverted()
            set_bone(c,rot)
            curl=aim.cross(Vector((0,1,0) if not left else (0,.4,1))).normalized()
            for finger in ("Thumb","Index","Middle","Ring","Little"):
                for j,joint in enumerate(("Proximal","Intermediate","Distal")):
                    name=side+finger+joint
                    if name in rest:
                        amount=(.04 if finger=="Thumb" else .22 if finger=="Index" else .38)*(j+1)
                        set_bone(name,Quaternion(curl,amount) @ rot)
        bpy.context.view_layer.update()
        return
    bank = -.095 if action=="AirLeft" else .095 if action=="AirRight" else 0.
    set_bone("Hips",Quaternion((0,1,0),bank))
    lean = .27 if crouch else .16 if stride else .07
    for bone, tilt in (("Spine",lean*.45),("Chest",lean),("UpperChest",lean),("Neck",.015),("Head",0)):
        set_bone(bone,Quaternion((1,0,0),tilt) @ Quaternion((0,1,0),bank*.7))
    for side,sign in (("Left",1),("Right",-1)):
        upper,lower,foot,toes = [side+n for n in ("UpperLeg","LowerLeg","Foot","Toes")]
        phase = fraction*2*math.pi+(0 if sign==1 else math.pi)
        step = math.cos(phase)*length if stride else 0
        lift = max(0,math.sin(phase))*(.155 if action.startswith("Run") else .073) if stride else 0
        target=rest[foot].head_local.copy()
        width=stance[0] if stance else .074 if not stride else .043
        stagger=stance[1] if stance else .060
        target.x+=sign*width+step*direction[0]
        target.y+=step*direction[1] + (-sign*stagger if not stride and not jump else 0)
        target.z+=lift
        if jump:
            target.y += -.14 if sign==1 else .12
            target.z += (.23 if sign==1 else .11) if action!="Fall" else (.11 if sign==1 else .06)
        knee_out = .09
        if crouch and stance:
            knee_out = {"Cantarella": .65, "BlackMiku": .50, "Mornye": .46}.get(garment, knee_out)
        two_bone(upper,lower,foot,target,Vector((sign*knee_out,-1,0)))
        set_bone(foot,Quaternion((1,0,0),.18*math.sin(phase) if stride else -.09 if jump else 0))
        set_bone(toes)
        set_bone(side+"Shoulder")
        a,b,c=[side+n for n in ("UpperArm","LowerArm","Hand")]
        shoulder=head(a)
        # Both hands follow the aiming line. Support hand is further forward.
        goal=Vector((-.09 if sign==1 else -.18, shoulder.y-(.385 if sign==1 else .255),
                     shoulder.z-(.145 if sign==1 else .19)))
        two_bone(a,b,c,goal,Vector((sign*.53,.12,-1)))
        set_bone(c,target=head(c)+Vector((-.13 if sign==1 else .16,-.80,-.46)),tip=None)
        for finger in ("Thumb","Index","Middle","Ring","Little"):
            for idx,joint in enumerate(("Proximal","Intermediate","Distal")):
                name=side+finger+joint
                if name in rest:
                    bend=(.18 if finger in ("Thumb","Index") else .45)*(idx+1)
                    set_bone(name,Quaternion((1,0,0),bend)@rotations[c])
    bpy.context.view_layer.update()

def author_actions(rig, garment=None):
    animation=rig.animation_data_create()
    bpy.context.scene.render.fps=30
    actions=[("Idle",60),("DuckIdle",48),("Rise",12),("Fall",15),("AirLeft",15),("AirRight",15),("Land",6)]
    for gait,frames in (("Run",22),("Walk",32),("Duck",34)):
        actions += [(gait+direction,frames) for direction in ("Forward","Back","Left","Right")]
    if garment: actions=[("Idle",72),("DuckIdle",60)]
    for name,length in actions:
        action=bpy.data.actions.new(name)
        action.use_fake_user=True
        animation.action=action
        for i in range(13 if name.startswith(("Run","Walk","Duck")) else 5):
            count=12 if name.startswith(("Run","Walk","Duck")) else 4
            pose(rig,name,i/count,1+length*i/count,garment)
    animation.action=None
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
