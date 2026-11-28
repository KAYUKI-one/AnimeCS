using System.Collections.Generic;
using AnimeCS.Content;
using AnimeCS.Match;
using StylizedCharacters;
using UnityEngine;

namespace AnimeCS.Play
{
    /// <summary>Connects compatible imported clothing to the live character skeleton on the instance only.</summary>
    public sealed class CharacterRig
    {
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        public Transform WeaponSocket { get; }
        public bool AimFromHand { get; }
        public Transform Head { get; }
        public Transform HeadMount { get; }
        private readonly List<(SkinnedMeshRenderer renderer, Mesh mesh, int stand, int crouch)> _drapes = new();
        private readonly Transform[] _feet = new Transform[4];
        private readonly Vector3[] _soleLocal = new Vector3[4];
        private readonly GroundedFeet _groundedFeet;
        private readonly Vector3 _faceLocal;
        internal Vector3 FaceCenter => Head.TransformPoint(_faceLocal);
        private readonly Transform[] _lookBones=new Transform[4];
        private readonly Quaternion[] _lookRest=new Quaternion[4];
        private bool _lookApplied;
        public Transform Chest { get; }
        internal WeaponGripRig Grip { get; }
        internal HumanoidSecondaryMotion SecondaryMotion { get; }
        private bool _hitApplied;
        private Quaternion _beforeHit;
        private readonly Animator _animator;
        private readonly HumanoidLocomotion _animation;
        private readonly UprightStance _stance;
        private LocomotionPose _locomotion;
        internal float BodyYaw => _locomotion.BodyYaw;
        internal float WaistYaw => _locomotion.Twist;
        public CharacterRig(GameObject body, CharacterDefinition definition, RuntimeAnimatorController stanceController=null,
            bool alwaysAnimate=false, GarmentRegions[] regions=null)
            : this(body, definition, stanceController, alwaysAnimate, regions, null) { }

        internal CharacterRig(GameObject body, CharacterDefinition definition, RuntimeAnimatorController stanceController,
            bool alwaysAnimate, GarmentRegions[] regions, SecondaryMotionProfile secondaryMotionProfile)
        {
            if (body == null) return;
            SecondaryMotion = HumanoidSecondaryMotion.Attach(body, secondaryMotionProfile);
            foreach (var bone in body.GetComponentsInChildren<Transform>(true))
            {
                // Ambiguous names cannot be safely retargeted.
                if (!_bones.TryAdd(bone.name, bone)) _bones[bone.name] = null;
            }
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (!mesh) continue;
                int stand = -1, crouch = -1;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    if (name.EndsWith("DrapeStand", System.StringComparison.Ordinal)) stand = i;
                    if (name.EndsWith("DrapeCrouch", System.StringComparison.Ordinal)) crouch = i;
                }
                if (stand >= 0 && crouch >= 0) _drapes.Add((renderer, mesh, stand, crouch));
            }
            FitDrape(0);
            var animator = body.GetComponentInChildren<Animator>(true);
            _animator = animator;
            if (definition != null && !string.IsNullOrWhiteSpace(definition.WeaponSocket))
                WeaponSocket = body.transform.Find(definition.WeaponSocket);
            if (animator && animator.avatar && animator.avatar.isValid && animator.isHuman)
            {
                _animation = new HumanoidLocomotion(animator);
                _stance = new UprightStance(animator);
                animator.cullingMode = alwaysAnimate ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
                if (!WeaponSocket) Grip = new WeaponGripRig(animator);
            }
            _bones.TryGetValue("Head",out var head); Head = head;
            _bones.TryGetValue("UpperChest",out var chest); Chest = chest;
            if(animator && animator.avatar && animator.avatar.isValid && animator.isHuman)
            {
                Head=animator.GetBoneTransform(HumanBodyBones.Head);
                _feet[0]=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _feet[1]=animator.GetBoneTransform(HumanBodyBones.RightFoot);
                _feet[2]=animator.GetBoneTransform(HumanBodyBones.LeftToes);
                _feet[3]=animator.GetBoneTransform(HumanBodyBones.RightToes);
                var sourceSkin = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Vector3[] calibrated = null;
                if (sourceSkin && regions != null)
                    foreach (var region in regions)
                        if (region.source == sourceSkin.sharedMesh) { calibrated = region.soleAnchors; break; }
                var skinBones = sourceSkin ? sourceSkin.bones : System.Array.Empty<Transform>();
                var bindPoses = sourceSkin && sourceSkin.sharedMesh ? sourceSkin.sharedMesh.bindposes : System.Array.Empty<Matrix4x4>();
                GroundedFeet.Foot leftFoot = null, rightFoot = null;
                for (int i=0;i<_feet.Length;i++)
                    if (_feet[i])
                    {
                        // A model containing animation takes can already be posed when instantiated.
                        int index = System.Array.IndexOf(skinBones,_feet[i]);
                        var boneToBody = index >= 0 && index < bindPoses.Length
                            ? body.transform.worldToLocalMatrix * sourceSkin.transform.localToWorldMatrix * bindPoses[index].inverse
                            : body.transform.worldToLocalMatrix * _feet[i].localToWorldMatrix;
                        var floorPoint = boneToBody.MultiplyPoint3x4(Vector3.zero);
                        floorPoint.y = 0;
                        var bodyToBone = boneToBody.inverse;
                        _soleLocal[i] = bodyToBone.MultiplyPoint3x4(floorPoint);
                        if (calibrated?.Length > i && calibrated[i] != Vector3.zero) _soleLocal[i] = calibrated[i];
                        if (i < 2)
                        {
                            var thigh = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                            var knee = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                            var foot = new GroundedFeet.Foot(animator.transform, thigh, knee, _feet[i], _soleLocal[i],
                                bodyToBone.MultiplyVector(Vector3.up));
                            if (i == 0) leftFoot = foot;
                            else rightFoot = foot;
                        }
                    }
                _groundedFeet = new GroundedFeet(leftFoot, rightFoot);
                Chest=animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
                _lookBones[0]=animator.GetBoneTransform(HumanBodyBones.Spine);_lookBones[1]=Chest;
                _lookBones[2]=animator.GetBoneTransform(HumanBodyBones.Neck);_lookBones[3]=Head;
            }
            if(Head)
            {
                if (definition && definition.FaceAnchor!=Vector3.zero) _faceLocal=definition.FaceAnchor;
                else
                {
                    var left=animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftEye) : null;
                    var right=animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightEye) : null;
                    var point=left && right ? (left.position+right.position)*.5f :
                        Head.position+body.transform.up*.058f+body.transform.forward*.069f;
                    _faceLocal=Head.InverseTransformPoint(point);
                }
                HeadMount=new GameObject("Head fittings").transform;HeadMount.SetParent(Head,false);
                HeadMount.rotation=body.transform.parent ? body.transform.parent.rotation : Quaternion.identity;
                CharacterFaceLighting.Bind(body, HeadMount);
            }
            var controller=stanceController ? stanceController : definition ? definition.Controller : null;
            if (controller && animator) animator.runtimeAnimatorController=controller;
            if (WeaponSocket == null && animator != null && animator.avatar != null && animator.avatar.isValid && animator.isHuman)
            {
                WeaponSocket = animator.GetBoneTransform(HumanBodyBones.RightHand);
                AimFromHand = WeaponSocket != null;
            }
        }

        internal void FitGround(IFootSurface surface, float floorHeight, bool grounded) =>
            _groundedFeet?.Apply(surface, floorHeight, grounded);

        internal void RestoreFootPose()
        {
            _groundedFeet?.Restore();
            _stance?.Restore();
            Grip?.RestoreClavicles();
        }

        internal void RelaxStance(float weight, IFootSurface surface = null, float floor = 0)
        {
            if (weight > 0 && _stance != null)
                _stance.Apply(weight, surface == null ? 0 : _groundedFeet?.DropBelow(surface, floor) ?? 0);
        }

        internal float LowestSole(float fallback)
        {
            if (!_feet[0] || !_feet[1]) return fallback;
            float lowest = float.PositiveInfinity;
            for (int i=0;i<_feet.Length;i++)
                if (_feet[i]) lowest = Mathf.Min(lowest, _feet[i].TransformPoint(_soleLocal[i]).y);
            return lowest;
        }

        public void FitDrape(float crouch)
        {
            float duck = 100f * Mathf.Clamp01(crouch);
            for(int i=0;i<_drapes.Count;i++)
            {
                var drape=_drapes[i];
                if(drape.renderer.sharedMesh!=drape.mesh)
                {
                    drape.mesh=drape.renderer.sharedMesh;drape.stand=drape.crouch=-1;
                    for(int shape=0;shape<drape.mesh.blendShapeCount;shape++)
                    {
                        string name=drape.mesh.GetBlendShapeName(shape);
                        if(name.EndsWith("DrapeStand",System.StringComparison.Ordinal))drape.stand=shape;
                        if(name.EndsWith("DrapeCrouch",System.StringComparison.Ordinal))drape.crouch=shape;
                    }
                    _drapes[i]=drape;
                }
                if(drape.stand>=0)drape.renderer.SetBlendShapeWeight(drape.stand,100f-duck);
                if(drape.crouch>=0)drape.renderer.SetBlendShapeWeight(drape.crouch,duck);
            }
        }

        public void RestoreHitPose()
        {
            if (_hitApplied && Chest) Chest.localRotation = _beforeHit;
            _hitApplied = false;
            if (_lookApplied) for (int i=0;i<_lookBones.Length;i++) if(_lookBones[i]) _lookBones[i].localRotation=_lookRest[i];
            _lookApplied=false;
        }

        public void Look(Transform basis,float yaw,float pitch)
        {
            if (_lookApplied || !Head) return;
            yaw=Mathf.Clamp(yaw,-58,58);pitch=Mathf.Clamp(pitch,-82,82);
            for (int i=0;i<_lookBones.Length;i++)
            {
                var bone=_lookBones[i];if(!bone)continue;
                _lookRest[i]=bone.localRotation;
                float wy=i==0?.17f:i==1?.33f:i==2?.18f:.32f;
                float wp=i==0?.12f:i==1?.24f:i==2?.23f:.36f;
                var right=Quaternion.AngleAxis(yaw,basis.up)*basis.right;
                bone.rotation=Quaternion.AngleAxis(-pitch*wp,right)*Quaternion.AngleAxis(yaw*wy,basis.up)*bone.rotation;
            }
            _lookApplied=true;
        }

        public void Flinch(float amount, HitZone zone)
        {
            if (amount <= .01f || !Chest || _hitApplied) return;
            _beforeHit = Chest.localRotation;
            Chest.rotation = Quaternion.AngleAxis(-amount, _animator.transform.right) *
                Quaternion.AngleAxis(zone == HitZone.Head ? amount*.65f : -amount*.32f, _animator.transform.forward) * Chest.rotation;
            _hitApplied = true;
        }

        public void Animate(in SeatView view, float runSpeed, float alpha = 0)
        {
            _locomotion.Sample(view, runSpeed, alpha);
            _animation?.Apply(_locomotion.Direction, _locomotion.Crouch, _locomotion.Vertical, _locomotion.AirTurn,
                view.Motion.Grounded, view.Match.Tick * SeatMovement.StepTime, _locomotion.JustReset,
                view.Match.Paused || view.Match.Phase == RoundPhase.Finished);
            FitDrape(_locomotion.Crouch);
        }

        public bool BindClothing(GameObject garment)
        {
            if (garment == null) return true;
            bool aligned = true;
            foreach (var skin in garment.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = skin.bones; bool complete = true;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null || !_bones.TryGetValue(bones[i].name, out var bone) || bone == null)
                    { complete = false; break; }
                    bones[i] = bone;
                }
                if (!complete) { aligned = false; continue; }
                skin.bones = bones;
                if (skin.rootBone != null && _bones.TryGetValue(skin.rootBone.name, out var root) && root != null)
                    skin.rootBone = root;
            }
            return aligned;
        }
    }
}
