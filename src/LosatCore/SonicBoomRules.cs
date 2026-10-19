namespace M8LOSATM2.LosatCore
{
    internal struct SonicBoomState {internal bool Initialized,Fired;internal float Started;}
    internal static class SonicBoomRules
    {
        internal const float MinimumDelay=.35f,SupersonicSpeed=343f,GainScale=.55f;
        internal static bool TryStart(ref SonicBoomState state,float now,float speed,bool alive,bool paused)
        {
            if(!alive || paused) return false;
            if(!state.Initialized){state.Initialized=true;state.Started=now;}
            if(state.Fired || now-state.Started<MinimumDelay || float.IsNaN(speed) || float.IsInfinity(speed) || speed<SupersonicSpeed) return false;
            state.Fired=true;return true;
        }
    }
}
