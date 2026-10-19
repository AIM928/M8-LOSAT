namespace M8LOSATM2.LosatCore
{
    // Engine-independent policy, exercised by the regression test executable.
    internal static class TargetLockRules
    {
        internal const float MaximumRange=5000f;
        internal const float VisibilityInterval=.1f;
        internal const float ObstructionGrace=1.25f;
        internal const float ReacquireSeconds=3f;
        internal const int MaximumTargets=4;
        internal static bool InView(float x,float y,float depth,float range)
        {
            return depth>0f && x>=.02f && x<=.98f && y>=.02f && y<=.98f &&
                range>2f && range<=MaximumRange;
        }
        internal static bool MustRelease(bool alive,bool ownSight,float obscuredSeconds)
        {
            return !alive || !ownSight || obscuredSeconds>=ObstructionGrace+ReacquireSeconds;
        }
        internal static bool Reacquiring(float obscuredSeconds) {return obscuredSeconds>=ObstructionGrace && obscuredSeconds<ObstructionGrace+ReacquireSeconds;}
        internal static bool FrameOn(bool reacquiring,float time) {return !reacquiring || time%.5f<.25f;}
    }
}
