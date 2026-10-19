using System;
namespace M8LOSATM2.LosatCore
{
    internal static class TargetPickRules
    {
        internal static bool PreferNeighbor(float pixelSquared,float directSquared)
        {return !float.IsNaN(pixelSquared) && !float.IsInfinity(pixelSquared) && pixelSquared>=0f && pixelSquared<=1024f && pixelSquared<directSquared;}
    }
}
