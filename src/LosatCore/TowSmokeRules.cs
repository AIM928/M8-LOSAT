using System;
namespace M8LOSATM2.LosatCore
{
    internal static class TowSmokeRules
    {
        internal static bool IsTail(string name,bool emissionEnabled)
        {return emissionEnabled && name!=null && name.IndexOf("Smoke Trail",StringComparison.OrdinalIgnoreCase)>=0;}
        internal static float Size(float original)
        {return Math.Min(original*2.4f,4.5f);}
        internal static float Lifetime(float original)
        {return Math.Min(original*1.8f,4.5f);}
        internal static float DistanceRate(string name)
        {return name.IndexOf("Side",StringComparison.OrdinalIgnoreCase)>=0 ? .06f : .10f;}
        internal static float StartAlpha(string name)
        {return name.IndexOf("Side",StringComparison.OrdinalIgnoreCase)>=0 ? .26f : .38f;}
        internal const int TailMaxParticles=256;
        internal static float MuzzleSize(float original) {return original*2.15f;}
        internal static float MuzzleLifetime(float original) {return Math.Min(original*1.8f,6.5f);}
    }
}
