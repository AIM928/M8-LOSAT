using System;
namespace M8LOSATM2.LosatCore
{
    internal static class ManualSteeringRules
    {
        internal static float Lead(float speed)
        {return float.IsNaN(speed)||float.IsInfinity(speed) ? 80f : Math.Max(80f,Math.Min(300f,speed*.18f));}
        internal static bool ReliableDistance(float distance)
        {return !float.IsNaN(distance) && !float.IsInfinity(distance) && distance>10f && distance<=5000f;}
        internal static float ArcHeight(float range)
        {return !ReliableDistance(range) || range<400f ? 0f : Math.Min(2.5f,range*.0015f);}
        internal static float ArcOffset(float progress,float range)
        {
            if(float.IsNaN(progress)||float.IsInfinity(progress) || progress<=0f || progress>=.65f) return 0f;
            float t=progress/.65f;
            // A smooth early crest; zero height AND slope by 65% of the range.
            return 16f*ArcHeight(range)*t*t*(1f-t)*(1f-t);
        }
        internal static bool Terminal(float progress,float remaining,float speed)
        {return progress>=.6f || remaining<=Math.Max(200f,Lead(speed)*2f);}
        internal static bool StableAim(float dot,float initialRange,float range)
        {return dot>=.9993908f && Math.Abs(initialRange-range)<=Math.Max(50f,initialRange*.2f);}
    }
}
