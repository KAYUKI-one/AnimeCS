using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Play
{
    // Poses the skinned arms after the locomotion animator has written its pose.
    // Contacts are middle knuckles in the authored display model's coordinate system.
    internal sealed class WeaponGripRig
    {
        private readonly Arm _right, _left;
        public bool Valid => _right.Ready && _left.Ready;

        public WeaponGripRig(Animator animator)
        {
            _right = new Arm(animator, false);
            _left = new Arm(animator, true);
        }

        public void Pose(Transform body, Transform mount, WeaponDefinition weapon, Quaternion aim, bool view, float swing=0, WeaponActionPose action=default, Bounds? insert=null)
        {
            if (!Valid || !mount || !weapon) return;
            if(weapon.Class==WeaponClass.Knife){PoseKnife(body,mount,weapon,aim,view,swing,action);return;}
            bool pistol = weapon.Class == WeaponClass.Pistol;
            var rotation = aim * (view ? Quaternion.Euler(pistol ? -3.5f : -6.5f, -7, 0) : Quaternion.identity) * Quaternion.Euler(action.Angles) * weapon.Rotation(view);
            var contact = view
                ? body.TransformPoint(new Vector3(pistol ? .185f : .168f, pistol ? -.194f : -.239f, pistol ? .39f : .337f))
                : (_right.Shoulder.position + _left.Shoulder.position) * .5f +
                  body.right * (pistol ? .045f : .098f) - body.up * (pistol ? .105f : .14f) +
                  aim * Vector3.forward * (pistol ? .375f : .22f);
            if (!view && weapon.ShoulderPad.sqrMagnitude > .0001f)
                contact = _right.Shoulder.position - body.right*.020f - body.up*.062f + body.forward*.075f
                    + rotation*(weapon.Grip-weapon.ShoulderPad);
            contact += (view ? body.rotation : aim) * (weapon.Offset(view) + action.Offset);
            mount.SetPositionAndRotation(contact - rotation * weapon.Grip, rotation);
            var rightPalm = Quaternion.LookRotation(new Vector3(.29f,-.06f,.96f),Vector3.up);
            bool wrap = pistol || weapon.UprightSupport;
            var leftPalm = wrap ? Quaternion.LookRotation(new Vector3(.07f,.14f,.99f),Vector3.up)
                : Quaternion.LookRotation(new Vector3(.85f,.18f,.49f),Vector3.forward);
            var rightTarget = _right.WristTarget(mount.TransformPoint(weapon.Grip),rotation * rightPalm);
            var support = weapon.Support;
            if (action.Release > 0)
            {
                var handling = Vector3.Lerp(weapon.Grip, weapon.Support, pistol ? .10f : .47f);
                handling += new Vector3(-.060f, -.09f - action.Handle * .105f, .015f);
                if (insert.HasValue)
                {
                    var b=insert.Value;
                    handling=b.center + new Vector3(-b.extents.x-.006f, -.013f, b.extents.z*.29f) + action.InsertOffset;
                }
                // Keep the passing wrist outside the prop's side during release and return.
                support = Vector3.Lerp(support, handling, action.Release);
                support.x -= Mathf.Sin(Mathf.PI * action.Release) * .064f;
                leftPalm = Quaternion.Slerp(leftPalm, Quaternion.LookRotation(new Vector3(.16f,.035f,.985f),Vector3.up),action.Release);
            }
            var leftTarget = _left.WristTarget(mount.TransformPoint(support),rotation * leftPalm);
            if (view)
            {
                // View sleeves use their own shoulder origins below the camera, preserving original bone lengths.
                _right.Shoulder.position = rightTarget + body.TransformDirection(new Vector3(.30f,-.43f,-.71f)) * _right.Reach;
                _left.Shoulder.position = leftTarget + body.TransformDirection(new Vector3(-.47f,-.40f,-.63f)) * _left.Reach;
            }
            else
            {
                _left.ReachFromClavicle(leftTarget);
                // Move the prop and both contacts together when a short arm or a steep aim would overextend.
                for (int i = 0; i < 3; i++)
                {
                    var delta = _left.ReachCorrection(leftTarget);
                    delta += _right.ReachCorrection(rightTarget + delta);
                    mount.position += delta;
                    rightTarget += delta; leftTarget += delta;
                }
            }
            _right.Solve(rightTarget, body.TransformDirection(new Vector3(.67f,-1,-.29f)),rotation * rightPalm);
            _left.Solve(leftTarget, body.TransformDirection(new Vector3(-.62f,-1,-.24f)),rotation * leftPalm);
            _right.PoseFingers(rotation, false, wrap);
            _left.PoseFingers(rotation, true, wrap);
            if (insert.HasValue && action.Release > 0)
            {
                var bounds=insert.Value; bounds.center+=action.InsertOffset;
                _left.CurlAroundInsert(mount,bounds,action.Release);
            }
        }

        private void PoseKnife(Transform body,Transform mount,WeaponDefinition knife,Quaternion aim,bool view,float swing,WeaponActionPose action)
        {
            var shoulderCenter=(_left.Shoulder.position+_right.Shoulder.position)*.5f;
            var right=view ? body.TransformPoint(new Vector3(.196f-.105f*swing,-.145f+.055f*swing,.35f+.035f*swing))
                : shoulderCenter+body.right*(.205f-.11f*swing)-body.up*.26f+aim*Vector3.forward*(.30f+.12f*swing);
            var left=view ? body.TransformPoint(new Vector3(-.195f,-.159f,.37f))
                : shoulderCenter-body.right*.24f-body.up*.105f+aim*Vector3.forward*.40f;
            right += aim * action.Offset;
            left += aim * action.Offset * .30f;
            var rotation=aim*Quaternion.Euler(action.Angles)*Quaternion.Euler(view ? -23 : -24,view ? -57-25*swing : -18-26*swing,view ? -17 : -12)*knife.Rotation(view);
            var rightPalm=rotation*Quaternion.LookRotation(new Vector3(.46f,.75f,.45f),Vector3.forward);
            var leftPalm=aim*Quaternion.LookRotation(new Vector3(.20f,.26f,.94f),Vector3.right);
            right+=aim*knife.Offset(view);
            var rw=_right.WristTarget(right,rightPalm);var lw=_left.WristTarget(left,leftPalm);
            if(view)
            {
                _right.Shoulder.position=rw+body.TransformDirection(new Vector3(.39f,-.41f,-.61f))*_right.Reach;
                _left.Shoulder.position=lw+body.TransformDirection(new Vector3(-.65f,-.36f,-.60f))*_left.Reach;
            }
            else
            {
                var correction=_right.ReachCorrection(rw);right+=correction;rw+=correction;
                lw+=_left.ReachCorrection(lw);
            }
            mount.SetPositionAndRotation(right-rotation*knife.Grip,rotation);
            _right.Solve(rw,body.TransformDirection(new Vector3(.85f,-.85f,-.34f)),rightPalm);
            _left.Solve(lw,body.TransformDirection(new Vector3(-.80f,-1,-.34f)),leftPalm);
            _right.KnifeFingers(mount,knife.Grip);
            _left.OpenFingers(aim);
        }

        private sealed class Arm
        {
            public readonly Transform Shoulder;
            private readonly Transform _elbow, _hand;
            private readonly Transform[] _fingers = new Transform[15];
            private readonly Vector3[] _localRay = new Vector3[15];
            private readonly float _upperLength, _lowerLength, _palmLength;
            private readonly Quaternion _palmBasis;
            public float Reach => _upperLength + _lowerLength;
            public bool Ready { get; }

            public Arm(Animator animator, bool left)
            {
                if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.isHuman) return;
                Shoulder = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                _elbow = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                _hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (!Shoulder || !_elbow || !_hand) return;
                var first = left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;
                for (int i = 0; i < _fingers.Length; i++)
                    _fingers[i] = animator.GetBoneTransform((HumanBodyBones)((int)first+i));
                var index = _fingers[3]; var middle = _fingers[6]; var little = _fingers[12];
                if (!index || !middle || !little) return;
                var along = middle.position - _hand.position;
                var across = index.position - little.position;
                _palmLength = along.magnitude;
                _palmBasis = Quaternion.Inverse(_hand.rotation) * Quaternion.LookRotation(along,across);
                _upperLength = Vector3.Distance(Shoulder.position,_elbow.position);
                _lowerLength = Vector3.Distance(_elbow.position,_hand.position);
                for (int i = 0; i < _fingers.Length; i++)
                {
                    var joint = _fingers[i];
                    if (!joint) continue;
                    Transform next = i%3<2 ? _fingers[i+1] : null;
                    var ray = next ? next.position-joint.position : joint.position-joint.parent.position;
                    _localRay[i] = Quaternion.Inverse(joint.rotation) * ray.normalized;
                }
                Ready = Reach > .1f && _palmLength > .015f;
            }

            public Vector3 WristTarget(Vector3 knuckle, Quaternion palm) => knuckle - palm*Vector3.forward*_palmLength;
            public Vector3 ReachCorrection(Vector3 target)
            {
                var ray = target-Shoulder.position;
                return Vector3.ClampMagnitude(ray,Reach-.006f)-ray;
            }

            public void ReachFromClavicle(Vector3 target)
            {
                var needed = -ReachCorrection(target);
                var joint = Shoulder.parent;
                if (!joint || needed.sqrMagnitude < .000004f) return;
                var arm = Shoulder.position-joint.position;
                var turn = Quaternion.FromToRotation(arm,arm+needed);
                joint.rotation = Quaternion.RotateTowards(Quaternion.identity,turn,30) * joint.rotation;
            }

            public void Solve(Vector3 target, Vector3 pole, Quaternion palm)
            {
                var ray = target - Shoulder.position;
                var axis = ray.normalized;
                var d = Mathf.Clamp(ray.magnitude, Mathf.Abs(_upperLength-_lowerLength)+.0001f,Reach-.0001f);
                float along = (_upperLength*_upperLength-_lowerLength*_lowerLength+d*d)/(2*d);
                var elbow = Shoulder.position + axis*along + Vector3.ProjectOnPlane(pole,axis).normalized *
                    Mathf.Sqrt(Mathf.Max(0,_upperLength*_upperLength-along*along));
                Shoulder.rotation = Quaternion.FromToRotation(_elbow.position-Shoulder.position,elbow-Shoulder.position) * Shoulder.rotation;
                _elbow.rotation = Quaternion.FromToRotation(_hand.position-_elbow.position,target-_elbow.position) * _elbow.rotation;
                _hand.rotation = palm * Quaternion.Inverse(_palmBasis);
            }

            public void CurlAroundInsert(Transform mount, Bounds bounds, float weight)
            {
                for(int finger=0;finger<5;finger++)
                {
                    var knuckle=_fingers[finger*3];if(!knuckle)continue;
                    var start=mount.InverseTransformPoint(knuckle.position);
                    float y=Mathf.Clamp(start.y,bounds.min.y+.004f,bounds.max.y-.004f);
                    // The fingers pass round the side silhouette; the thumb closes on the opposite face.
                    float front=Mathf.Min(bounds.max.z+.004f,start.z+.024f);
                    float back=Mathf.Max(bounds.min.z-.003f,start.z-.028f);
                    for(int link=0;link<3;link++)
                    {
                        int i=finger*3+link;var joint=_fingers[i];if(!joint)continue;
                        var local = finger==0 ? link switch
                        {
                            0=>new Vector3(bounds.min.x-.003f,y+.008f,back),
                            1=>new Vector3(bounds.center.x,y+.005f,back),
                            _=>new Vector3(bounds.max.x+.002f,y,back+.010f)
                        } : link switch
                        {
                            0=>new Vector3(bounds.min.x-.003f,y,front),
                            1=>new Vector3(bounds.center.x+.002f,y-.002f,front+.003f),
                            _=>new Vector3(bounds.max.x+.003f,y-.003f,front-.011f)
                        };
                        Vector3 ray=mount.TransformPoint(local)-joint.position;
                        if(ray.sqrMagnitude<.000001f)continue;
                        var turn=Quaternion.FromToRotation(joint.rotation*_localRay[i],ray);
                        joint.rotation=Quaternion.Slerp(Quaternion.identity,turn,weight)*joint.rotation;
                    }
                }
            }

            public void KnifeFingers(Transform mount,Vector3 contact)
            {
                for(int finger=0;finger<5;finger++)
                {
                    var first=_fingers[finger*3];if(!first)continue;
                    float z=mount.InverseTransformPoint(first.position).z;
                    for(int link=0;link<3;link++)
                    {
                        int i=finger*3+link;var joint=_fingers[i];if(!joint)continue;
                        var onGrip=finger==0 ? link switch {
                            0=> new Vector3(-.023f,-.006f,contact.z+.029f), 1=>new Vector3(-.019f,.005f,contact.z+.044f),
                            _=>new Vector3(-.007f,.013f,contact.z+.049f) }
                            : link switch { 0=>new Vector3(.009f,.017f,z),1=>new Vector3(.018f,-.008f,z),_=>new Vector3(-.003f,-.017f,z) };
                        var direction=mount.TransformPoint(onGrip)-joint.position;
                        if(direction.sqrMagnitude>.0000001f)
                            joint.rotation=Quaternion.FromToRotation(joint.rotation*_localRay[i],direction)*joint.rotation;
                    }
                }
            }
            public void OpenFingers(Quaternion aim)
            {
                for(int i=0;i<_fingers.Length;i++)
                {
                    var joint=_fingers[i];if(!joint)continue;
                    int finger=i/3,link=i%3;
                    var ray=finger==0 ? new Vector3(.76f,-.12f-link*.08f,.55f) :
                        new Vector3(.18f+(2-finger)*.096f,.32f-link*.33f,.96f);
                    joint.rotation=Quaternion.FromToRotation(joint.rotation*_localRay[i],aim*ray)*joint.rotation;
                }
            }
            public void PoseFingers(Quaternion aim, bool left, bool wrap)
            {
                for (int i=0; i<_fingers.Length; i++)
                {
                    var joint = _fingers[i];
                    if (!joint) continue;
                    int finger = i/3, link = i%3;
                    Vector3 dir;
                    if (finger == 0)
                        dir = left ? (wrap ? new Vector3(.18f,link==0 ? .66f : .19f,1) :
                            link==0 ? new Vector3(.52f,.74f,.46f) : new Vector3(.10f,.45f,.88f))
                                   : (link==0 ? new Vector3(-.65f,.43f,.68f) : new Vector3(.03f,.13f,1));
                    else if (!left && finger == 1)
                        dir = new Vector3(-.06f,.17f,1);
                    else if (left && !wrap)
                        dir = link==0 ? new Vector3(.04f,1,-.06f) : link==1 ? new Vector3(-.83f,.55f,-.08f) : new Vector3(-1,-.3f,-.07f);
                    else
                    {
                        float side = left ? 1 : -1;
                        dir = link==0 ? new Vector3(.76f*side,-.16f,.62f) :
                              link==1 ? new Vector3(.95f*side,-.08f,-.32f) : new Vector3(.10f*side,.04f,-1);
                    }
                    joint.rotation = Quaternion.FromToRotation(joint.rotation*_localRay[i], aim*dir) * joint.rotation;
                }
            }
        }
    }
}
