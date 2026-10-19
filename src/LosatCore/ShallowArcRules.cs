using System;
namespace M8LOSATM2.LosatCore
{
    internal static class ShallowArcRules
    {
        internal static float Height(float range)
        {return float.IsNaN(range)||float.IsInfinity(range)||range<100f ? 0f : Math.Min(12f,range*.004f);}
        internal static float Offset(float progress,float range)
        {
            if(float.IsNaN(progress)||float.IsInfinity(progress)) return 0f;
            float t=Math.Max(0f,Math.Min(1f,progress));
            return 4f*Height(range)*t*(1f-t);
        }
    }
}
