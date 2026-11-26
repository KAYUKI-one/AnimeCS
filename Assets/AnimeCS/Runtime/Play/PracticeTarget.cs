using AnimeCS.Content;
using AnimeCS.Match;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class PracticeTarget : ISeatDriver
    {
        private readonly LocalMatch _session;
        private readonly Vector3 _station;
        private readonly float _yaw;
        public PracticeTarget(LocalMatch session, MapDefinition map, int seat)
        {
            _session=session;
            _station=map.Spawn(Side.Defend,seat%LocalMatch.TeamSize);
            _yaw=map.SpawnYaw(Side.Defend,seat%LocalMatch.TeamSize);
        }

        public SeatIntent Decide(in SeatView view)
        {
            float offset=_session.MovingTargets ? Mathf.Sin(view.Match.Tick*.025f+view.Seat)*1.4f : 0;
            float delta=_station.x+offset-view.Position.x;
            return new SeatIntent { Yaw=_yaw, Walk=true, Move=new Vector2(Mathf.Abs(delta)>.10f?Mathf.Clamp(delta*2,-1,1):0,0) };
        }
    }
}
