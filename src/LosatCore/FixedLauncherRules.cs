using System;
namespace M8LOSATM2.LosatCore
{
    internal static class FixedLauncherRules
    {
        internal const float HalfSector=15f;
        internal const float TubeElevation=3f;
        internal const float CommandTurnRate=2f;
        internal static bool Allowed(float yaw)
        {return !float.IsNaN(yaw) && !float.IsInfinity(yaw) && Math.Abs(yaw)<=HalfSector+.0001f;}
        internal static float HullCorrection(float yaw)
        {return Allowed(yaw) ? 0f : yaw>0 ? yaw-HalfSector : yaw+HalfSector;}
        internal static float GripFloor(float original,bool side)
        {return float.IsNaN(original)||float.IsInfinity(original)||original<=0 ? original : Math.Max(original,side ? 1.3f : 1.2f);}
        internal static float SpinRatio(float nativeRadius,float visualRadius)
        {return nativeRadius>0 && visualRadius>0 ? Math.Max(.25f,Math.Min(2f,nativeRadius/visualRadius)) : 1f;}
    }
}
