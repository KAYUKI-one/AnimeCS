using System.Collections;
using AnimeCS.Match;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AnimeCS.Content;
using System.Linq;

namespace AnimeCS.Tests
{
    public sealed class PawnMovementTests
    {
        [UnityTest] public IEnumerator AThrownDisplayMeshRestsAboveTheFloorAndOnTheNearSideOfAWall()
        {
            var root=new GameObject("Drop clearance test");root.transform.position=new Vector3(1800,110,1700);
            var map=ScriptableObject.CreateInstance<MapDefinition>();var gun=ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                GameObject Block(Vector3 pos,Vector3 size)
                {
                    var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.transform.SetParent(root.transform,false);
                    g.transform.localPosition=pos;g.transform.localScale=size;return g;
                }
                Block(Vector3.down*.25f,new Vector3(8,.5f,8));Block(new Vector3(0,.8f,1.10f),new Vector3(5,1.6f,.16f));
                var mesh=Block(Vector3.zero,new Vector3(.07f,.22f,.84f));mesh.transform.localPosition=new Vector3(.02f,.06f,.19f);
                mesh.SetActive(false);typeof(WeaponDefinition).GetField("_worldModel",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(gun,mesh);
                var go=new GameObject("Seat");go.transform.SetParent(root.transform,false);var seat=go.AddComponent<ArenaPawn>();
                seat.Spawn(root.transform.position+Vector3.up*.035f,0);
                var world=new ArenaWorld(new[]{seat},map,root.transform);
                Physics.SyncTransforms();
                foreach(float yaw in new[]{0f,48f,90f,180f})
                {
                    seat.Move(Vector2.zero,yaw,0,false,false);
                    Assert.That(world.TryRestingPose(0,gun,true,null,out var rest),Is.True);var bounds=ArenaWorld.ModelBounds(mesh);
                    var middle=rest.position+rest.rotation*bounds.center;
                    Assert.That(Physics.CheckBox(middle,bounds.extents*.985f,rest.rotation,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),Is.False);
                    Assert.That(middle.y-root.transform.position.y,Is.InRange(.035f,.060f));
                    if(yaw==0)
                    {
                        Assert.That(middle.z+Mathf.Abs((rest.rotation*bounds.extents).z),Is.LessThan(root.transform.position.z+1.03f));
                        world.DropPath(0,gun,rest,out var from,out var top);
                        var drop=new GroundPickup(rest,new WeaponInstance(gun,null),0,from,top);
                        for(int tick=0;tick<20;tick++)
                            Assert.That(Physics.CheckBox(drop.At(tick)+rest.rotation*bounds.center,bounds.extents*.98f,rest.rotation,
                                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),Is.False,"drop path "+tick);
                        Assert.That(world.TryRestingPose(0,gun,true,new[]{drop},out var second),Is.True);
                        Assert.That(Vector3.Distance(rest.position,second.position),Is.GreaterThan(.10f));
                    }
                    Assert.That(world.TryRestingPose(0,gun,true,null,out var next),Is.True);Assert.That(Vector3.Distance(next.position,rest.position),Is.LessThan(.001f));
                }
                world.Dispose();
            }
            finally {Object.Destroy(root);Object.Destroy(map);Object.Destroy(gun);}
            yield return null;
        }

        [UnityTest] public IEnumerator HitscanProducesSurfaceMarksAndSuppressorsOmitTheTrail()
        {
            var root=new GameObject("Effect test room");
            root.transform.position=new Vector3(1400,120,1400);
            var style=ScriptableObject.CreateInstance<MatchPresentation>();
            ArenaWorld feedbackWorld = null;
            var surface=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            style.tracerMaterial=style.impactMaterial=surface;
            var map=ScriptableObject.CreateInstance<MapDefinition>();
            try
            {
                ArenaPawn Make(int i)
                {
                    var g=new GameObject("Seat "+i);g.transform.SetParent(root.transform,false);
                    return g.AddComponent<ArenaPawn>();
                }
                var pawns=new[]{Make(0),Make(1)};
                var world=feedbackWorld=new ArenaWorld(pawns,map,root.transform,style);
                world.Spawn(0,root.transform.position,0);
                world.Spawn(1,root.transform.TransformPoint(new Vector3(3,0,1)),0);
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform,false);wall.transform.localPosition=new Vector3(0,1.5f,5);
                wall.transform.localScale=new Vector3(3,3,.3f);
                Physics.SyncTransforms();world.Advance(1);
                Assert.That(world.Trace(0,Vector2.zero,false),Is.Null);
                world.RenderFeedback(0);
                var line=root.GetComponentInChildren<LineRenderer>();
                Assert.That(line.enabled,Is.True);
                float firstHead=line.GetPosition(1).z;
                Assert.That(firstHead,Is.LessThan(wall.transform.position.z-.15f));
                world.Advance(3);world.RenderFeedback(.5f);
                Assert.That(line.GetPosition(1).z,Is.GreaterThan(firstHead));
                Assert.That(line.GetPosition(1).z,Is.LessThanOrEqualTo(wall.transform.position.z-.14f));
                Assert.That(world.SurfaceMarkCount,Is.EqualTo(1));
                world.Advance(15);Assert.That(line.enabled,Is.False);
                world.Spawn(1,root.transform.TransformPoint(new Vector3(0,0,2)),180);
                Physics.SyncTransforms();
                Assert.That(world.Trace(0,Vector2.zero,true)?.Seat,Is.EqualTo(1));
                world.RenderFeedback(0);
                Assert.That(line.enabled,Is.False);
                Assert.That(world.SurfaceMarkCount,Is.EqualTo(1));
                world.ResetRound();
                Assert.That(world.SurfaceMarkCount,Is.Zero);
            }
            finally { feedbackWorld?.Dispose(); Object.Destroy(root);Object.Destroy(style);Object.Destroy(surface);Object.Destroy(map); }
            yield return null;
        }

        [UnityTest] public IEnumerator CameraHeightClearsACoveredMuzzleInBothStances()
        {
            var root=new GameObject("Optical clearance");root.transform.position=new Vector3(1740,88,1920);
            var map=ScriptableObject.CreateInstance<MapDefinition>();
            GameObject Block(Vector3 size,Vector3 where)
            {
                var box=GameObject.CreatePrimitive(PrimitiveType.Cube);box.transform.SetParent(root.transform,false);
                box.transform.localScale=size;box.transform.localPosition=where;return box;
            }
            try
            {
                Block(new Vector3(8,.4f,14),new Vector3(0,-.2f,4));
                var actors=new ArenaPawn[2];
                for(int i=0;i<2;i++){var obj=new GameObject("Seat "+i);obj.transform.SetParent(root.transform,false);actors[i]=obj.AddComponent<ArenaPawn>();}
                var world=new ArenaWorld(actors,map,root.transform);
                world.Spawn(0,root.transform.position+Vector3.up*.035f,0);
                world.Spawn(1,root.transform.position+new Vector3(0,.035f,7),180);
                var cover=Block(new Vector3(3,1,.24f),new Vector3(0,.5f,1));
                world.SyncMovement();
                foreach(bool crouch in new[]{false,true})
                {
                    for(int i=0;i<30;i++){world.Move(0,Vector2.zero,0,0,false,crouch);world.Move(1,Vector2.zero,180,0,false,crouch);}
                    float eye=actors[0].Eye.y-root.transform.position.y;
                    Assert.That(eye,Is.EqualTo(crouch?1.16f:1.62f).Within(.055f));
                    void CoverTo(float h){cover.transform.localScale=new Vector3(3,h,.24f);cover.transform.localPosition=new Vector3(0,h*.5f,1);world.SyncMovement();}
                    world.SetMuzzle(0,actors[0].Eye+new Vector3(.16f,-.31f,.48f));CoverTo(eye-.028f);
                    Assert.That(world.Trace(0,Vector2.zero,false)?.Zone,Is.EqualTo(HitZone.Head));
                    CoverTo(eye+.028f);Assert.That(world.Trace(0,Vector2.zero,false),Is.Null);
                    world.SetLocalView(actors[0].Eye+Vector3.up*.05f);
                    Assert.That(world.Trace(0,Vector2.zero,false)?.Zone,Is.EqualTo(HitZone.Head),"Interpolated camera is already over the lip.");
                    world.SetLocalView(null);CoverTo(.2f);
                    Assert.That(world.Trace(0,new Vector2(2,0),false),Is.Null,"Hair and shoulders to the side of the head are outside its hitbox.");
                }
            }
            finally { Object.Destroy(root);Object.Destroy(map); }
            yield return null;
        }

        [UnityTest] public IEnumerator CapsuleClearsAJumpAndStaysBelowALowCeiling()
        {
            var root = new GameObject("Locomotion test room");
            root.transform.position = new Vector3(1200,100,1200);
            GameObject Block(Vector3 local,Vector3 scale)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.transform.SetParent(root.transform,false);
                block.transform.localPosition = local; block.transform.localScale = scale;
                return block;
            }
            try
            {
                Block(new Vector3(0,-.25f,0),new Vector3(18,.5f,18));
                var actor = new GameObject("Test seat");
                actor.transform.SetParent(root.transform,false);
                var pawn = actor.AddComponent<ArenaPawn>();
                pawn.Spawn(root.transform.position+Vector3.up*.035f,0);
                Physics.SyncTransforms();
                MotionContact state = default;
                for(int i=0;i<6;i++) state=pawn.Move(Vector2.zero,0,0,false,false);
                Assert.That(state.Grounded,Is.True);
                float ground = pawn.transform.position.y, peak = ground;
                var sawFall = false;
                for(int i=0;i<45;i++)
                {
                    state=pawn.Move(new Vector2(.1f,.4f),i*4,0,i==0,false);
                    peak=Mathf.Max(peak,pawn.transform.position.y);
                    if (state.VerticalSpeed < -1 && !state.Grounded) sawFall = true;
                }
                Assert.That(sawFall && state.Grounded,Is.True);
                Assert.That(peak-ground,Is.InRange(.65f,.9f));
                Assert.That(pawn.transform.position.y-ground,Is.EqualTo(0).Within(.035f));
                for(int i=0;i<10;i++) state=pawn.Move(Vector2.zero,0,0,false,true);
                Assert.That(pawn.Height,Is.EqualTo(SeatMovement.DuckHeight).Within(.001f));
                Assert.That(pawn.Eye.y-pawn.transform.position.y,Is.EqualTo(1.16f).Within(.001f));
                var roof=Block(new Vector3(0,1.58f,0),new Vector3(4,.30f,4));
                Physics.SyncTransforms();
                for(int i=0;i<18;i++) pawn.Move(Vector2.zero,0,0,false,false);
                Assert.That(pawn.Height,Is.InRange(1.31f,1.43f));
                roof.SetActive(false);
                for(int i=0;i<18;i++) pawn.Move(Vector2.zero,0,0,false,false);
                Assert.That(pawn.Height,Is.EqualTo(SeatMovement.StandingHeight).Within(.001f));
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
    }
}
