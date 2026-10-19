using System;
namespace M8LOSATM2.LosatCore
{
    internal static class ViewportScanRules
    {
        internal const int VisibilityChecksPerFrame=2;
        internal const int CandidateChecksPerFrame=32;
        internal const float ScanLifetime=1f;
        internal static bool CandidateInView(float x,float y,float depth,float range)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(y) && !float.IsInfinity(y) &&
                !float.IsNaN(depth) && !float.IsInfinity(depth) && !float.IsNaN(range) && !float.IsInfinity(range) &&
                depth>0 && x>=-.1f && x<=1.1f && y>=-.1f && y<=1.1f && range>2 && range<=TargetLockRules.MaximumRange;
        }
        internal static float CenterScore(float x,float y)
        {float dx=x-.5f,dy=y-.5f;return dx*dx+dy*dy;}
        internal static bool SameView(float angle,float fovDifference)
        {return !float.IsNaN(angle) && !float.IsNaN(fovDifference) && angle<=1f && Math.Abs(fovDifference)<=.05f;}
    }
}
