namespace M8LOSATM2.LosatCore
{
    internal static class LockRecoveryRules
    {
        internal static bool Update(ref float obscuredSince,ref float clearSince,bool visible,bool hardBlocked,float now)
        {
            if(visible)
            {
                if(clearSince<0f) clearSince=now;
                if(obscuredSince>=0f && now-clearSince<.2f) return false;
                obscuredSince=-1f;
            }
            else if(hardBlocked)
            {
                clearSince=-1f;
                if(obscuredSince<0f) obscuredSince=now;
            }
            else clearSince=-1f; // An uncertain/saturated ray is not confirmed recovery.
            return visible;
        }
        // Extra rays run only after center and the five cheap body samples fail.
        // Cover the full body silhouette rather than a small area near its center.
        internal const int SearchSamples=8;
        internal static float SearchX(int i) {return (i&1)==0 ? -.92f : .92f;}
        internal static float SearchY(int i) {return (i&2)==0 ? .75f : -.30f;}
        internal static float SearchZ(int i) {return (i&4)==0 ? -.92f : .92f;}
        internal static float SearchPadding(float size) {return System.Math.Min(60f,System.Math.Max(24f,size*.6f));}
    }
}
