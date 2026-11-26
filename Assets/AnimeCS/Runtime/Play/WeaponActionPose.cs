using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    // Animation offsets live in aim space; grip contacts stay in each skin's model space.
    internal struct WeaponActionPose
    {
        internal Vector3 Offset, Angles, InsertOffset;
        internal float Release, Handle, Dip;
        internal const float DrawSeconds = .35f;
        internal const float SwapPoint = .31f;

        internal static float DrawProgress(in SeatView seat, float alpha) =>
            Mathf.Clamp01((seat.Match.Tick + alpha - seat.EquipTick) * SeatMovement.StepTime / DrawSeconds);

        private static float Ease(float from, float to, float time) =>
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(from, to, time));

        internal static WeaponActionPose Sample(in SeatView seat, WeaponDefinition weapon, float alpha, bool view, float scale)
        {
            var pose = new WeaponActionPose();
            if (!weapon) return pose;
            float frame = seat.Match.Tick + alpha;
            if (seat.ReloadEnd > seat.ReloadStart && frame < seat.ReloadEnd)
            {
                float t = Mathf.InverseLerp(seat.ReloadStart, seat.ReloadEnd, frame);
                float tilt = Ease(0,.20f,t) * (1 - Ease(.72f,1,t));
                pose.Release = Ease(.09f,.30f,t) * (1 - Ease(.68f,.90f,t));
                pose.Handle = Ease(.34f,.48f,t) * (1 - Ease(.53f,.71f,t));
                float travel = Ease(.22f,.44f,t) * (1 - Ease(.56f,.76f,t));
                pose.InsertOffset = new Vector3(-.033f, weapon.Class==WeaponClass.Pistol ? -.126f : -.166f, -.016f) * travel;
                pose.Offset = new Vector3(.025f,.032f,.035f) * tilt;
                pose.Angles = new Vector3(-11,-12,-28) * tilt;
            }
            else
            {
                bool knife = weapon.Class == WeaponClass.Knife;
                float t = (frame - seat.InspectTick) * SeatMovement.StepTime / (knife ? 1.65f : 2.25f);
                if (t >= 0 && t < 1)
                {
                    float show = Ease(0,.22f,t) * (1 - Ease(.79f,1,t));
                    float turn = Ease(.35f,.62f,t);
                    pose.Offset = new Vector3(knife ? -.055f : -.075f,.058f,.045f) * show;
                    pose.Angles = new Vector3(-12 + 9*turn, -29 + 15*turn, (knife ? -50 : -36) + 52*turn) * show;
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
