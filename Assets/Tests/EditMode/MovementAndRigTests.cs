using System.Linq;
using AnimeCS.Content;
using AnimeCS.Editor;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using StylizedCharacters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace AnimeCS.Tests
{
    public sealed class MovementAndRigTests
    {
        [Test] public void PlayableOutfitsShareLayeredLocomotionAndKeepTheirDrapeCorrectives()
        {
            const string path = "Assets/Content/Art/Characters/Locomotion.controller";
            var shared = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.That(shared, Is.Not.Null);
            Assert.That(shared.layers[0].avatarMask, Is.Null, "The base layer must animate the legs.");
            var upper = shared.layers.Single(layer => layer.name == "Upper Body");
            Assert.That(upper.stateMachine.states, Is.Not.Empty);
            Assert.That(upper.avatarMask, Is.Not.Null);
            foreach (var part in new[] { AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
                AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK })
                Assert.That(upper.avatarMask.GetHumanoidBodyPartActive(part), Is.False, part.ToString());
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm })
                Assert.That(upper.avatarMask.GetHumanoidBodyPartActive(part), Is.True, part.ToString());

            var catalog = ContentLibrary.Catalog;
            Assert.That(catalog.SeatOutfits.Count, Is.EqualTo(8));
            foreach (var outfit in catalog.SeatOutfits)
            {
                Assert.That(outfit.Character.Controller, Is.SameAs(shared), outfit.Id);
                Assert.That(outfit.PoseController, Is.SameAs(shared), outfit.Id);
                if (outfit.Character.Id is "character.rover" or "character.michele" or "character.nora") continue;
                var instance=Object.Instantiate(outfit.Character.Model);
                try
                {
                    var rig=new CharacterRig(instance,outfit.Character,shared);
                    var renderer=instance.GetComponentsInChildren<SkinnedMeshRenderer>()
                        .Single(candidate => candidate.sharedMesh.GetBlendShapeIndex("DrapeStand") >= 0);
                    var region=outfit.Regions(false).First(r=>r.source==renderer.sharedMesh);
                    int idle=renderer.sharedMesh.GetBlendShapeIndex("DrapeStand"),duck=renderer.sharedMesh.GetBlendShapeIndex("DrapeCrouch");
                    Assert.That(idle,Is.GreaterThanOrEqualTo(0));Assert.That(duck,Is.GreaterThanOrEqualTo(0));
                    Assert.That(region.partitioned.GetBlendShapeIndex("DrapeCrouch"),Is.EqualTo(duck));
                    renderer.sharedMesh=region.partitioned; rig.FitDrape(.65f);
                    Assert.That(renderer.GetBlendShapeWeight(idle),Is.EqualTo(35).Within(.01));
                    Assert.That(renderer.GetBlendShapeWeight(duck),Is.EqualTo(65).Within(.01));
                }
                finally {Object.DestroyImmediate(instance);}
            }
        }

        [Test] public void StationaryTwistStartsTurningAtFortyEightDegreesAndMovementBlendsIn()
        {
            const float speed = 6f;
            var pose = new LocomotionPose();
            var seat = new SeatView { Match = new PublicMatchView { Round = 1 }, Motion = MotionContact.Standing };
            pose.Sample(seat, speed, 0f);

            seat.Match.Tick++;
            seat.Yaw = 47f;
            pose.Sample(seat, speed, 0f);
            Assert.That(pose.BodyYaw, Is.EqualTo(0f).Within(.001f));
            Assert.That(pose.Twist, Is.EqualTo(47f).Within(.001f));
            Assert.That(pose.Direction, Is.EqualTo(Vector2.zero));

            seat.Match.Tick++;
            seat.Yaw = 48f;
            pose.Sample(seat, speed, 0f);
            Assert.That(pose.BodyYaw, Is.GreaterThan(0f).And.LessThan(48f));
            Assert.That(pose.Twist, Is.GreaterThan(0f).And.LessThan(48f));
            Assert.That(pose.Direction.x, Is.GreaterThan(0f));
            for (int i = 0; i < 60; i++)
            {
                seat.Match.Tick++;
                pose.Sample(seat, speed, 0f);
            }
            Assert.That(pose.BodyYaw, Is.EqualTo(48f).Within(.001f));
            Assert.That(pose.Twist, Is.EqualTo(0f).Within(.001f));
            Assert.That(pose.Direction.sqrMagnitude, Is.LessThan(.0001f));

            seat.Motion.Velocity = Vector2.right * speed;
            seat.Match.Tick++;
            pose.Sample(seat, speed, 0f);
            float initialBlend = pose.Direction.magnitude;
            Assert.That(initialBlend, Is.GreaterThan(0f).And.LessThan(.5f));
            for (int i = 0; i < 30; i++)
            {
                seat.Match.Tick++;
                pose.Sample(seat, speed, 0f);
            }
            Assert.That(pose.Direction.magnitude, Is.EqualTo(1f).Within(.01f));
        }

        [Test] public void EveryPlayableOutfitBakesSkinnedFirstPersonTrousersAndShoesOnly()
        {
            var outfits = ContentLibrary.Catalog.SeatOutfits;
            Assert.That(outfits.Count, Is.EqualTo(8));
            foreach (var outfit in outfits)
            {
                bool hasBottom = false, hasShoes = false;
                foreach (var region in outfit.Regions(false))
                {
                    bool IsLower(int slot) => slot == (int)WearSlot.Bottom || slot == (int)WearSlot.Shoes;
                    bool containsLower = region.slots.Any(IsLower);
                    if (!containsLower)
                    {
                        Assert.That(region.firstPersonLower, Is.Null, outfit.Id);
                        continue;
                    }

                    var mesh = region.firstPersonLower;
                    Assert.That(mesh, Is.Not.Null, outfit.Id);
                    Assert.That(mesh.bindposes, Is.EqualTo(region.partitioned.bindposes), outfit.Id);
                    Assert.That(region.lowerMaterialSlots.Length, Is.EqualTo(mesh.subMeshCount), outfit.Id);
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        int sourceSlot = region.lowerMaterialSlots[i];
                        Assert.That(sourceSlot, Is.InRange(0, region.slots.Length - 1), outfit.Id);
                        int slot = region.slots[sourceSlot];
                        Assert.That(IsLower(slot), Is.True, outfit.Id + " lower mesh contains a non-leg material slot");
                        Assert.That(mesh.GetIndexCount(i), Is.Positive, outfit.Id);
                        hasBottom |= slot == (int)WearSlot.Bottom;
                        hasShoes |= slot == (int)WearSlot.Shoes;
                    }
                }
                Assert.That(hasBottom && hasShoes, Is.True, outfit.Id + " must draw both lower clothing and shoes");
            }
        }

        [TestCase("michele", Side.Defend, .00005f, .05666f, .07153f, 1.49250f)]
        [TestCase("nora", Side.Attack, -.00116f, .05129f, .08780f, 1.44736f)]
        public void NewCastHasMeasuredEyesAndKeepsItsLowerBodyWhenHeadMeshIsMasked(
            string id, Side side, float eyeX, float eyeY, float eyeZ, float standingEyes)
        {
            var catalog = ContentLibrary.Catalog;
            var outfit = catalog.Find<OutfitDefinition>("outfit." + id);
            Assert.That(outfit, Is.Not.Null, id);
            Assert.That(outfit.Character, Is.SameAs(catalog.Find<CharacterDefinition>("character." + id)));
            Assert.That(outfit.Character.Faction, Is.EqualTo(side));
            Assert.That(Vector3.Distance(outfit.Character.FaceAnchor, new Vector3(eyeX, eyeY, eyeZ)), Is.LessThan(.001f));
            Assert.That(outfit.Character.StandingEyes, Is.EqualTo(standingEyes).Within(.002f));
            Assert.That(outfit.Included, Is.True);
            Assert.That(outfit.Arms.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, Is.Positive);
            Assert.That(outfit.ViewArms(true).GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, Is.Positive);
            Assert.That(outfit.ViewRig.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh.vertexCount, Is.Positive);
            Assert.That(outfit.Regions(true), Is.Not.Empty, id + " sleeve regions");
            Assert.That(outfit.Regions(true).Any(region => region.slots.Contains((int)WearSlot.Gloves)), Is.True, id);

            var body = Object.Instantiate(outfit.Character.Model);
            try
            {
                var region = outfit.Regions(false).Single(item => item.firstPersonLower);
                var skin = body.GetComponentsInChildren<SkinnedMeshRenderer>().Single(renderer => renderer.sharedMesh == region.source);
                skin.sharedMesh = region.partitioned;
                skin.sharedMaterials = region.materials;
                var headgear = new GameObject("Head accessory");
                headgear.transform.SetParent(body.transform, false);
                var headRenderer = headgear.AddComponent<MeshRenderer>();
                var perspective = new FirstPersonBody(body, outfit, new System.Collections.Generic.List<Renderer>());
                try
                {
                    perspective.SetLocal(true);
                    var visible = skin.transform.Find("First-person body").GetComponent<SkinnedMeshRenderer>();
                    var shadow = skin.transform.Find("Full character shadow").GetComponent<SkinnedMeshRenderer>();
                    int layer = LayerMask.NameToLayer(CharacterCamera.HiddenLayer);
                    Assert.That(layer, Is.GreaterThanOrEqualTo(0));
                    Assert.That(headRenderer.gameObject.layer, Is.EqualTo(layer));
                    Assert.That(skin.gameObject.layer, Is.EqualTo(layer));
                    Assert.That(visible.gameObject.layer, Is.Not.EqualTo(layer));
                    Assert.That(visible.sharedMesh.triangles.Length, Is.Positive);
                    Assert.That(visible.bones, Is.EqualTo(skin.bones));
                    Assert.That(visible.sharedMesh.triangles.Length, Is.LessThan(region.partitioned.triangles.Length));
                    Assert.That(shadow.sharedMesh, Is.SameAs(region.partitioned));
                    Assert.That(shadow.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly));
                    perspective.SetLocal(false);
                    Assert.That(skin.sharedMesh, Is.SameAs(region.partitioned));
                    Assert.That(skin.gameObject.layer, Is.Not.EqualTo(layer));
                    Assert.That(visible.enabled || shadow.enabled, Is.False);
                }
                finally { perspective.Release(); }
            }
            finally { Object.DestroyImmediate(body); }
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void CounterTapStopsWithinFiveTicksWithoutReverseDisplacement(int original)
        {
            const float speed = 6;
            var motion = new MotionContact { Grounded = true, Velocity = Vector2.right * (original * speed) };
            var reverse = new SeatIntent { Move = Vector2.left * original };
            bool stopped = false;
            for (int i = 0; i < 5; i++)
            {
                motion.Velocity = SeatMovement.Advance(motion, reverse, speed, ref stopped);
                Assert.That(motion.Velocity.x * original, Is.GreaterThanOrEqualTo(0));
                if (i == 3) Assert.That(Mathf.Abs(motion.Velocity.x), Is.GreaterThan(.01f));
            }
            Assert.That(motion.Velocity, Is.EqualTo(Vector2.zero));
            var held = motion;
            bool dwell = stopped;
            for (int i = 0; i < 10; i++)
                held.Velocity = SeatMovement.Advance(held, reverse, speed, ref dwell);
            Assert.That(held.Velocity.x * original, Is.EqualTo(-speed).Within(.001));
            float drift = 0;
            for (int i = 0; i < 15; i++)
            {
                motion.Velocity = SeatMovement.Advance(motion, default, speed, ref stopped);
                drift += motion.Velocity.x * SeatMovement.StepTime;
            }
            Assert.That(drift, Is.Zero);
        }

        [Test] public void NeutralReleaseAndSubtickTapHaveDistinctBraking()
        {
            const float runSpeed = 6f;
            const float steadyFraction = .34f;
            const float floatTolerance = .00001f;
            var tuning = new MovementTuning();
            int steadyTick = Mathf.CeilToInt(tuning.coastToSteadySeconds / SeatMovement.StepTime);
            bool stopped = false;
            var moving = new MotionContact { Grounded = true, Velocity = Vector2.right * runSpeed };
            var oneTap = new SeatIntent { BrakeTap = Vector2.left };
            var tapped = SeatMovement.Advance(moving, oneTap, runSpeed, ref stopped, tuning);
            var released = SeatMovement.Advance(moving, default, runSpeed, ref stopped, tuning);
            Assert.That(tapped.x, Is.LessThan(released.x));

            for (int tick = 1; tick <= steadyTick; tick++)
            {
                float previousSpeed = moving.Velocity.x;
                moving.Velocity = SeatMovement.Advance(moving, default, runSpeed, ref stopped, tuning);
                Assert.That(moving.Velocity.x, Is.LessThan(previousSpeed), $"Expected less speed at tick {tick}.");
                if (tick < steadyTick)
                    Assert.That(moving.Velocity.x / runSpeed, Is.GreaterThan(steadyFraction + floatTolerance));
            }

            Assert.That(moving.Velocity.x / runSpeed, Is.LessThanOrEqualTo(steadyFraction + floatTolerance));
            Assert.That(SeatMovement.Advance(MotionContact.Standing, oneTap, runSpeed, ref stopped, tuning),
                Is.EqualTo(Vector2.zero));
        }

        [Test] public void CrouchPeekLowersTheEyeAndSpeedTogetherWhileContinuingLaterally()
        {
            var m = MotionContact.Standing; var tuning=new MovementTuning();
            bool stopped=false;
            var run=new SeatIntent{Move=Vector2.right};var walk=new SeatIntent{Move=Vector2.right,Walk=true};
            for(int i=0;i<20;i++)m.Velocity=SeatMovement.Advance(m,run,6,ref stopped,tuning);
            Assert.That(m.Velocity.x,Is.EqualTo(6).Within(.001));
            for(int i=0;i<20;i++)m.Velocity=SeatMovement.Advance(m,walk,6,ref stopped,tuning);
            Assert.That(m.Velocity.x,Is.EqualTo(6*tuning.walkFraction).Within(.001));
            var peek=new SeatIntent{Move=Vector2.right,Crouch=true,Walk=true};
            float last=m.Velocity.x;
            for(int i=0;i<12;i++)
            {
                m.Velocity=SeatMovement.Advance(m,peek,6,ref stopped,tuning);
                m.Crouch=Mathf.MoveTowards(m.Crouch,1,SeatMovement.StepTime/tuning.crouchSeconds);
                Assert.That(m.Velocity.x,Is.GreaterThan(0).And.LessThanOrEqualTo(last+.0001f));last=m.Velocity.x;
            }
            Assert.That(last,Is.EqualTo(6*tuning.duckFraction).Within(.001));
            Assert.That(SeatMovement.EyeHeight(m.Crouch),Is.EqualTo(1.16f).Within(.001));
        }

        [Test] public void AirTurnsPreserveMomentumAndRespectProjectedAcceleration()
        {
            var m = new MotionContact { Grounded = false, Velocity = new Vector2(0,6) };
            bool stopped = false;
            Assert.That(SeatMovement.Advance(m, new SeatIntent { Yaw = 90 }, 6, ref stopped), Is.EqualTo(m.Velocity));
            var side = new SeatIntent { Move = Vector2.right };
            for (int i=0; i<14; i++) m.Velocity = SeatMovement.Advance(m,side,6,ref stopped);
            Assert.That(m.Velocity.x, Is.EqualTo(1.08f).Within(.001));
            Assert.That(m.Velocity.y, Is.EqualTo(6).Within(.001));
            m.Grounded = true;
            m.Velocity = new Vector2(2, -3);
            var diagonal = SeatMovement.Advance(m,new SeatIntent { Move = Vector2.up },6,ref stopped);
            Assert.That(diagonal.x, Is.GreaterThan(0)); Assert.That(diagonal.y, Is.LessThan(0));
        }

        [Test] public void ReloadRigPreservesOriginalSilhouetteAndMovesOnlyDuringAnimation()
        {
            foreach (var gun in ContentLibrary.Catalog.Entries.OfType<WeaponDefinition>().Where(g=>g.Class!=WeaponClass.Knife))
            {
                var original=ArenaWorld.ModelBounds(gun.WorldModel);
                var rig=gun.PosedModel(true);var bound=ArenaWorld.ModelBounds(rig);
                Assert.That(Vector3.Distance(original.size,bound.size),Is.LessThan(.002f),gun.Id);
                Assert.That(Vector3.Distance(original.center,bound.center),Is.LessThan(.002f),gun.Id);
                Assert.That(rig.GetComponentsInChildren<MeshRenderer>().Count(r=>r.name=="ReloadInsert"),Is.EqualTo(1),gun.Id);
                if(gun.Id=="weapon.ember47")
                {
                    var parts=rig.GetComponentsInChildren<MeshRenderer>();
                    float receiver=parts.Single(part=>part.name=="Body").bounds.min.y;
                    float magazine=parts.Single(part=>part.name=="ReloadInsert").bounds.min.y;
                    Assert.That(receiver-magazine,Is.GreaterThan(.04f),"The inner magazine shell must leave with its ribbed cover.");
                }
                var seat=new SeatView{Match=new PublicMatchView{Tick=60},EquipTick=-1000,InspectTick=-1000,ReloadStart=10,ReloadEnd=110};
                Assert.That(WeaponActionPose.Sample(seat,gun,0,true,1).InsertOffset.magnitude,Is.GreaterThan(.1f),gun.Id);
                seat.Match.Tick=110;Assert.That(WeaponActionPose.Sample(seat,gun,0,true,1).InsertOffset,Is.EqualTo(Vector3.zero));
            }
        }

        [Test] public void EachDisplayMeshUsesItsOwnContactsOnBothArmRigs()
        {
            var catalog=ContentLibrary.Catalog;
            var weapons=catalog.Entries.OfType<WeaponDefinition>().ToArray();
            Assert.That(weapons.Select(w=>w.WorldModel).Distinct().Count(),Is.EqualTo(weapons.Length));
            foreach (var outfit in catalog.SeatOutfits)
            foreach (bool view in new[]{false,true})
            {
                var root=new GameObject("Pose test");
                try
                {
                    var model=Object.Instantiate(view ? outfit.ViewRig : outfit.Character.Model,root.transform,false);
                    var animator=model.GetComponent<Animator>();
                    Assert.That(animator.avatar.isValid && animator.isHuman,Is.True);
                    var hands=new WeaponGripRig(animator);
                    Assert.That(hands.Valid,Is.True,outfit.name);
                    var mount=new GameObject("Weapon").transform; mount.SetParent(root.transform,false);
                    var idle=outfit.Character.Controller.animationClips.First(c=>c.name=="Idle");
                    var graph=PlayableGraph.Create();
                    try
                    {
                        var output=AnimationPlayableOutput.Create(graph,"Pose",animator);
                        output.SetSourcePlayable(AnimationClipPlayable.Create(graph,idle)); graph.Play();
                        foreach (var gun in weapons)
                        foreach (float pitch in new[]{0f,34f,-28f})
                        foreach (int actionIndex in new[]{0,1,2,3})
                        {
                            graph.Evaluate(.015f);
                            var aim=Quaternion.Euler(pitch,15,-3);
                            var seat=new SeatView{Match=new PublicMatchView{Tick=50}, EquipTick=actionIndex==3?45:-500,
                                InspectTick=actionIndex==1?9:-500,ReloadStart=actionIndex==2?20:0,ReloadEnd=actionIndex==2?84:0};
                            var action=WeaponActionPose.Sample(seat,gun,.4f,view,1f);
                            hands.Pose(root.transform,mount,gun,aim,view,0,action);
                            foreach(bool left in new[]{false,true})
                            {
                                var bone=animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                                if(left && gun.Class==WeaponClass.Knife)
                                {
                                    Assert.That(Vector3.Distance(bone.position,mount.TransformPoint(gun.Grip)),Is.GreaterThan(.19f),outfit.name+" knife offhand");
                                    continue;
                                }
                                if(left && action.Release>.8f) // The support hand deliberately leaves its idle grip during a reload.
                                {
                                    Assert.That(Vector3.Distance(bone.position,mount.TransformPoint(gun.Support)),Is.GreaterThan(.05f));
                                    continue;
                                }
                                float error=Vector3.Distance(bone.position,mount.TransformPoint(left ? gun.Support : gun.Grip));
                                Assert.That(error,Is.LessThan(.012f),$"{outfit.name}, {gun.name}, view={view}, left={left}, pitch={pitch}");
                            }
                        }
                    }
                    finally { graph.Destroy(); }
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [Test] public void AllPlayableAvatarsRetargetCrouchWithoutMovingThePlantedFoot()
        {
            var catalog = ContentLibrary.Catalog;
            Assert.That(catalog.SeatOutfits.Count,Is.EqualTo(8));
            var clips = catalog.Character.Controller.animationClips.Distinct().ToDictionary(c => c.name);
            Assert.That(clips.Count,Is.EqualTo(19));
            foreach (var outfit in catalog.SeatOutfits)
            {
                Assert.That(outfit.Arms.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(1000));
                var body = Object.Instantiate(outfit.Character.Model);
                var baked = new Mesh();
                var graph = PlayableGraph.Create();
                try
                {
                    var animator = body.GetComponent<Animator>();
                    Assert.That(animator.avatar.isValid && animator.avatar.isHuman,Is.True);
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.applyRootMotion = false;
                    var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.That(skin.bones.Length,Is.GreaterThanOrEqualTo(22));
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph,"Pose",animator);
                    void Sample(string name)
                    {
                        var playable = AnimationClipPlayable.Create(graph,clips[name]);
                        output.SetSourcePlayable(playable); playable.SetTime(.15);
                        graph.Play(); graph.Evaluate(0);
                        skin.BakeMesh(baked);
                    }
                    Sample("Idle");
                    Vector3 hip = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Vector3 foot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                    var standing = baked.vertices;
                    Sample("DuckIdle");
                    Assert.That(hip.y-animator.GetBoneTransform(HumanBodyBones.Hips).position.y, Is.InRange(.32f,.57f),outfit.DisplayName);
                    Assert.That(Vector3.Distance(foot,animator.GetBoneTransform(HumanBodyBones.RightFoot).position),Is.LessThan(.06f));
                    var duck = baked.vertices;
                    int moved = 0;
                    for(int i=0;i<duck.Length;i+=8) if ((standing[i]-duck[i]).sqrMagnitude > .008f) moved++;
                    Assert.That(moved,Is.GreaterThan(duck.Length/32),outfit.DisplayName);
                }
                finally { graph.Destroy(); Object.DestroyImmediate(baked); Object.DestroyImmediate(body); }
            }
        }

        [Test] public void WeaponContactsSurviveStatureChangesAfterBinding()
        {
            var catalog = ContentLibrary.Catalog;
            var weapons = new[]
            {
                catalog.StartingPistol(Side.Attack),
                catalog.Entries.OfType<WeaponDefinition>().First(weapon => weapon.Class == WeaponClass.HeavySniper)
            };
            foreach (var outfit in catalog.SeatOutfits)
            {
                var root = new GameObject("Scaled IK test");
                var graph = PlayableGraph.Create();
                try
                {
                    root.transform.rotation = Quaternion.Euler(0, -21, 0);
                    var stature = Bone(root.transform, "Stature", Vector3.zero);
                    stature.localScale = new Vector3(1.04f, 1.08f, 1.04f);
                    var body = Object.Instantiate(outfit.Character.Model, stature, false);
                    var animator = body.GetComponentInChildren<Animator>();
                    var rig = new WeaponGripRig(animator);
                    Assert.That(rig.Valid, Is.True, outfit.Id);
                    var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                    var controller = outfit.Character.Controller;
                    var mount = Bone(root.transform, "Weapon", Vector3.zero);
                    foreach (float height in new[] { .76f, 1.18f })
                    {
                        stature.localScale = new Vector3(1.04f, height, 1.04f);
                        var clip = controller.animationClips.First(value => value.name == (height < 1 ? "DuckIdle" : "Idle"));
                        output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                        graph.Play();
                        foreach (var weapon in weapons)
                        foreach (float pitch in new[] { -32f, 0f, 44f })
                        {
                            graph.Evaluate(.017f);
                            rig.Pose(root.transform, mount, weapon, Quaternion.Euler(pitch, -8, 0), false);
                            var right = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                            var left = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                            float error = Mathf.Max(Vector3.Distance(right.position, mount.TransformPoint(weapon.Grip)),
                                Vector3.Distance(left.position, mount.TransformPoint(weapon.Support)));
                            Assert.That(error, Is.LessThan(.015f), $"{outfit.Id}, {weapon.Id}, height={height}, pitch={pitch}");
                        }
                    }
                }
                finally { graph.Destroy(); Object.DestroyImmediate(root); }
            }
        }

        [Test] public void FeetFollowTheSurfaceIndependentlyAndRestoreTheAirbornePose()
        {
            var root = new GameObject("Foot IK test");
            try
            {
                var ankleToSole = Vector3.down * .11f;
                GroundedFeet.Foot MakeFoot(float x, out Transform ankle)
                {
                    var thigh = Bone(root.transform, "Thigh", new Vector3(x, .92f, 0));
                    var knee = Bone(thigh, "Knee", new Vector3(0, -.39f, .18f));
                    ankle = Bone(knee, "Ankle", new Vector3(0, -.40f, -.14f));
                    return new GroundedFeet.Foot(root.transform, thigh, knee, ankle, ankleToSole, Vector3.up);
                }

                var left = MakeFoot(-.22f, out var leftAnkle);
                var right = MakeFoot(.22f, out var rightAnkle);
                var feet = new GroundedFeet(left, right);
                var slope = new TestFootSurface(new Plane(new Vector3(-.3f, 1, 0).normalized, Vector3.up * .04f));
                var originalLeft = leftAnkle.position;
                var originalRight = rightAnkle.position;
                feet.Apply(slope, .02f, true);
                Assert.That(slope.Queries, Is.EqualTo(2));
                Assert.That(slope.Plane.GetDistanceToPoint(leftAnkle.TransformPoint(ankleToSole)), Is.EqualTo(.006f).Within(.003f));
                Assert.That(slope.Plane.GetDistanceToPoint(rightAnkle.TransformPoint(ankleToSole)), Is.EqualTo(.006f).Within(.003f));
                Assert.That(Vector3.Angle(leftAnkle.up, slope.Plane.normal), Is.LessThan(1));
                Assert.That(rightAnkle.position.y - leftAnkle.position.y, Is.GreaterThan(.10f));
                Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));

                feet.Apply(slope, .02f, true);
                Assert.That(slope.Queries, Is.EqualTo(2), "One late pose pass may solve each foot once.");
                feet.Apply(slope, .02f, false);
                Assert.That(Vector3.Distance(originalLeft, leftAnkle.position), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(originalRight, rightAnkle.position), Is.LessThan(.0001f));

                rightAnkle.localPosition += Vector3.up * .18f;
                var swing = rightAnkle.position;
                feet.Apply(slope, .02f, true);
                Assert.That(slope.Queries, Is.EqualTo(3), "Lifted animation feet skip the terrain probe.");
                Assert.That(rightAnkle.position, Is.EqualTo(swing));

            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Transform Bone(Transform parent, string name, Vector3 localPosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            return bone;
        }

        private sealed class TestFootSurface : IFootSurface
        {
            internal Plane Plane { get; }
            internal int Queries { get; private set; }
            internal TestFootSurface(Plane plane) { Plane = plane; }

            public bool TryFind(Vector3 origin, float distance, out Vector3 point, out Vector3 normal)
            {
                Queries++;
                var ray = new Ray(origin, Vector3.down);
                normal = Plane.normal;
                bool hit = Plane.Raycast(ray, out float travel) && travel <= distance;
                point = hit ? ray.GetPoint(travel) : default;
                return hit;
            }
        }
    }
}
