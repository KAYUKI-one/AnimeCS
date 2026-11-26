using UnityEngine;

namespace AnimeCS.Match
{
    public static class ShotPattern
    {
        // Unsigned integer avalanche is stable across managed and IL2CPP players.
        private static uint Mix(uint x)
        {
            unchecked { x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;return x^(x>>16); }
        }
        public static Vector2 Sample(int matchSeed,int seat,int sequence,float spreadDegrees,bool centerWeighted=false)
        {
            uint key=Mix((uint)matchSeed ^ Mix((uint)seat+1) ^ Mix((uint)sequence+0x85ebca6bu));
            float angle=(key>>8)*(2f*Mathf.PI/16777216f);
            float sample=(Mix(key+0x9e3779b9u)>>8)/16777216f;
            float radius=(centerWeighted?sample:Mathf.Sqrt(sample))*spreadDegrees;
            return new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
        }
    }
}
