using System;
namespace M8LOSATM2.LosatCore
{
    internal static class ImpactSmokeRules
    {
        internal const int PoolSize=6,MaximumSmokeLayers=2,MaximumParticles=18;
        internal const float LeaseSeconds=9f;
        internal static bool IsSmoke(string name)
        {return name!=null && (name.IndexOf("smoke",StringComparison.OrdinalIgnoreCase)>=0 || name.IndexOf("dust",StringComparison.OrdinalIgnoreCase)>=0);}
        internal static bool IsFlash(string name)
        {return name!=null && name.IndexOf("flash",StringComparison.OrdinalIgnoreCase)>=0;}
        internal static int SourceScore(string name,bool primary)
        {
            if(!IsSmoke(name) || IsFlash(name)) return -1;
            bool smoke=name.IndexOf("smoke",StringComparison.OrdinalIgnoreCase)>=0;
            bool wave=name.IndexOf("wave",StringComparison.OrdinalIgnoreCase)>=0;
            return primary ? smoke ? 100 : 50 : wave ? 100 : smoke ? 30 : 70;
        }
        internal static int BurstCount(int maximum,bool flash,int layer,float distanceSquared)
        {
            if(flash) return 1;
            int primary=18,support=10;
            // Conservative thresholds: near and medium views keep the dense
            // first plume; distant views reduce unresolvable overlap, not size.
            if(distanceSquared>=2500f*2500f) {primary=8;support=4;}
            else if(distanceSquared>=1200f*1200f) {primary=12;support=6;}
            return Math.Max(1,Math.Min(maximum,layer==0 ? primary : support));
        }
        internal static float DetailDistanceSquared(float distanceSquared,float fieldOfView)
        {
            if(float.IsNaN(distanceSquared)||distanceSquared<0f || float.IsNaN(fieldOfView)||fieldOfView<=0f||fieldOfView>=179f) return 0f;
            // Equal projected size receives equal detail. A zoomed distant hit
            // is not treated as a tiny exterior-view effect.
            double ratio=Math.Tan(fieldOfView*Math.PI/360d)/Math.Tan(Math.PI/6d);
            return (float)(distanceSquared*ratio*ratio);
        }
        internal static float Lifetime(float original,bool flash) {return flash ? .14f : Math.Min(Math.Max(original*1.25f,4f),8f);}
        internal static float LayerLifetime(float original,bool flash,int layer)
        {float life=Lifetime(original,flash);return !flash && layer>0 ? Math.Min(life,3.5f) : life;}
        internal static float Size(float original,bool flash) {return flash ? Math.Min(original,2f) : Math.Min(Math.Max(original*.85f,2.5f),8f);}
        internal static bool AcceptHit(bool ownRound,bool spall,bool alreadyShown,bool vehicleHit,bool selfHit)
        {return ownRound && !spall && !alreadyShown && vehicleHit && !selfHit;}
        internal static int AvailableSlot(float[] until,float now)
        {for(int i=0;i<until.Length;i++) if(until[i]<=now) return i;return -1;}
    }
}
