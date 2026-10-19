using System;

namespace M8LOSATM2.LosatCore
{
    internal struct HudBox
    {
        internal float X,Y,W,H;
        internal HudBox(float x,float y,float w,float h) {X=x;Y=y;W=w;H=h;}
        internal bool Overlaps(HudBox b)
        {return X<b.X+b.W+3 && X+W+3>b.X && Y<b.Y+b.H+3 && Y+H+3>b.Y;}
    }
    internal static class SightHudRules
    {
        internal static HudBox TubePanel(HudBox view,float screenHeight)
        {
            // Leave the compass clear even when the main camera has an overscan viewport.
            return Clamp(new HudBox(view.X+view.W*.5f-105f,Math.Max(view.Y+24f,screenHeight*.14f),210f,43f),view);
        }
        internal static float FrameWidth(float projected) {return Math.Max(16f,Math.Min(90f,projected*.5f+2f));}
        internal static float FrameHeight(float projected) {return Math.Max(14f,Math.Min(64f,projected*.5f+2f));}
        private static HudBox Clamp(HudBox b,HudBox view)
        {
            b.W=Math.Min(b.W,view.W);b.H=Math.Min(b.H,view.H);
            b.X=Math.Max(view.X,Math.Min(view.X+view.W-b.W,b.X));
            b.Y=Math.Max(view.Y,Math.Min(view.Y+view.H-b.H,b.Y));return b;
        }
        internal static HudBox Place(HudBox anchor,HudBox view,HudBox[] used,int count,float width,float height,int slot)
        {
            // Bounded work for four labels: no sorting, allocation or world scans.
            for(int i=0;i<24;i++)
            {
                float x=i%2==0 ? anchor.X+anchor.W+5 : anchor.X-width-5;
                float y=anchor.Y-22+(i/2)*24;
                HudBox candidate=Clamp(new HudBox(x,y,width,height),view);
                bool clear=true;
                for(int j=0;j<count;j++) if(candidate.Overlaps(used[j])) {clear=false;break;}
                if(clear) return candidate;
            }
            // Safe, separate rows if an unusually tiny/edge viewport clamps all candidates.
            return Clamp(new HudBox(view.X+view.W-width-4,view.Y+70+slot*24,width,height),view);
        }
    }
    internal sealed class TubeLoadState
    {
        internal const float ReloadSeconds=30f;
        internal int Mask {get;private set;}
        internal int DamagedMask {get;private set;}
        internal TubeLoadState() {Reloaded(SalvoSequence.TubeCount);}
        internal void Reloaded(int capacity)
        {int count=Math.Max(0,Math.Min(SalvoSequence.TubeCount,capacity));Mask=((1<<count)-1)&~DamagedMask;}
        internal void DamageTube(int index)
        {if(index>=0 && index<SalvoSequence.TubeCount){DamagedMask|=1<<index;Mask&=~(1<<index);}}
        internal bool Damaged(int index) {return index>=0 && index<SalvoSequence.TubeCount && (DamagedMask&(1<<index))!=0;}
        internal void FiredTube(int index)
        {if(index>=0 && index<SalvoSequence.TubeCount) Mask &= ~(1<<index);}
        internal bool Loaded(int index) {return index>=0 && index<SalvoSequence.TubeCount && (Mask&(1<<index))!=0;}
        internal static int Remaining(float total,float elapsed)
        {return (int)Math.Ceiling(Math.Max(0f,total-elapsed));}
    }
}
