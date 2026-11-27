using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    // Animation offsets live in aim space; grip contacts stay in each skin's model space.
    internal struct WeaponActionPose
    {
        internal Vector3 Offset, Angles, InsertOffset;
        internal float Release, Handle, Dip, Hinge;
        internal const float DrawSeconds = .35f;
        internal const float SwapPoint = .31f;

        internal static float DrawProgress(in SeatView seat, float alpha) =>
            Mathf.Clamp01((seat.Match.Tick + alpha - seat.EquipTick) * SeatMovement.StepTime / DrawSeconds);

        private static float Ease(float from, float to, float time) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, time));

        internal static float InspectSeconds(InspectStyle style) =>
            style is InspectStyle.StraightBlade or InspectStyle.RingBlade or InspectStyle.FoldingBlade?2.15f:2.65f;

        private static void Inspect(ref WeaponActionPose pose,InspectStyle style,float seconds)
        {
            bool blade=style is InspectStyle.StraightBlade or InspectStyle.RingBlade or InspectStyle.FoldingBlade;
            float t=seconds/InspectSeconds(style);
            if(t<0 || t>=1)return;
            float show=Ease(0,.24f,t)*(1-Ease(.78f,1,t));
            float second=Ease(.40f,.62f,t);
            Vector3 first,reverse;
            switch(style)
            {
                case InspectStyle.StraightBlade: first=new(-17,27,-48);reverse=new(-6,-18,30);break;
                case InspectStyle.RingBlade: first=new(-21,39,-94);reverse=new(-3,-24,53);break;
                case InspectStyle.FoldingBlade:
                    float flick=Ease(.14f,.26f,t)*(1-Ease(.28f,.44f,t));
                    pose.Hinge=114f*flick;
                    first=new(19,31,-62);reverse=new(-15,-27,41);break;
                case InspectStyle.Sidearm: first=new(-19,-36,-33);reverse=new(-8,-20,14);break;
                case InspectStyle.Precision: first=new(-7,-32,-22);reverse=new(-3,-24,-8);break;
                default: first=new(-12,-46,-29);reverse=new(-5,-35,3);break;
            }
            pose.Offset=new Vector3(blade?-.038f:-.075f,blade?.046f:.058f,.033f)*show;
            pose.Angles=Vector3.Lerp(first,reverse,second)*show;
        }

        private static void Apply(ref WeaponActionPose pose, HandlingTrack track, float elapsed)
        {
            if (track == null || !track.Sample(elapsed, out var sample)) return;
            pose.Offset += sample.offset;
            pose.Angles += sample.angles;
            pose.Hinge += sample.hinge;
        }

        internal static WeaponActionPose Sample(in SeatView seat, WeaponDefinition weapon, float alpha, bool view, float scale, HeldItemActions actions = null)
        {
            var pose = new WeaponActionPose();
            float frame = seat.Match.Tick + alpha;
            if (weapon && seat.ReloadEnd > seat.ReloadStart && frame < seat.ReloadEnd)
            {
                float t = Mathf.InverseLerp(seat.ReloadStart, seat.ReloadEnd, frame);
                bool compact=weapon.Class==WeaponClass.Pistol;
                float tilt = Ease(0,.18f,t) * (1 - Ease(.78f,1,t));
                pose.Release = Ease(.04f,.21f,t) * (1 - Ease(.78f,.96f,t));
                pose.Handle = Ease(.34f,.53f,t) * (1 - Ease(.59f,.74f,t));
                float travel = Ease(.20f,.43f,t) * (1 - Ease(.57f,.75f,t));
                float seatTap=Ease(.70f,.75f,t)*(1-Ease(.75f,.83f,t));
                pose.InsertOffset = new Vector3(-.048f,compact?-.132f:-.177f,-.037f)*travel + Vector3.up*.011f*seatTap;
                pose.Offset = new Vector3(.018f,.031f,.035f)*tilt+Vector3.up*.013f*seatTap;
                pose.Angles = new Vector3(-10,-14,compact?-32:-25)*tilt+new Vector3(-3,0,4)*seatTap;
            }
            else
            {
                float inspectAge = (frame - seat.InspectTick) * SeatMovement.StepTime;
                if (actions?.inspect != null) Apply(ref pose, actions.inspect, inspectAge);
                else if (weapon) Inspect(ref pose, weapon.Inspect, inspectAge);
                float age=(frame-seat.LastShotTick)*SeatMovement.StepTime;
                if (weapon && weapon.Class == WeaponClass.Knife && seat.LastShotTick >= seat.EquipTick)
                    Apply(ref pose, actions?.Attack(seat.LastMelee), age);
                if(weapon && weapon.Class!=WeaponClass.Knife && seat.LastShotTick>=seat.EquipTick && age>=0 && age<.24f)
                {
                    float kick=Mathf.Exp(-age*28f)*Mathf.Cos(age*40f);
                    float side=(seat.LastShotTick%3-1)*.75f;
                    pose.Offset+=new Vector3(side*.004f,.006f,-.036f)*kick;
                    pose.Angles+=new Vector3(-3.3f,side,1.65f*side)*kick;
                }
            }
            float draw = DrawProgress(seat,alpha);
            pose.Dip = draw < SwapPoint ? Ease(0,SwapPoint,draw) : 1 - Ease(SwapPoint,1,draw);
            pose.Offset += new Vector3(.036f, view ? -.29f : -.13f, .025f) * pose.Dip;
            pose.Angles += new Vector3(22, 9, -13) * pose.Dip;
            if (!view) { pose.Angles *= .56f; pose.Offset *= .65f; }
            pose.Angles *= scale; pose.Offset *= scale; pose.InsertOffset *= scale; pose.Release *= scale; pose.Handle *= scale;
            return pose;
        }
    }
}
