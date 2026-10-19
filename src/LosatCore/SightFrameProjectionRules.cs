using System;
namespace M8LOSATM2.LosatCore
{
    internal static class SightFrameProjectionRules
    {
        internal static float ZoomScale(int index)
        {return index==0 ? 4f/50f : index==1 ? 8f/50f : index==2 ? 24f/50f : 1f;}
        internal static float GuiX(float normalized,HudBox view) {return view.X+normalized*view.W;}
        internal static float GuiY(float normalized,HudBox view) {return view.Y+(1f-normalized)*view.H;}
        internal static bool Fresh(int captured,int current) {return captured>=0 && current-captured>=0 && current-captured<=1;}
    }
}
