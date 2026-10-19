using System;
namespace M8LOSATM2.LosatCore
{
    internal static class SightStatusRules
    {
        private static readonly string[] Zoom={"ZOOM 4x","ZOOM 8x","ZOOM 24x","ZOOM 50x"};
        private static readonly string[] Loaded=TubeLabels("R");
        private static readonly string[] Empty=TubeLabels("E");
        private static readonly string[] Damaged=TubeLabels("X");
        private static string[] TubeLabels(string status)
        {var labels=new string[SalvoSequence.TubeCount];for(int i=0;i<labels.Length;i++)labels[i]=(i<6?"L":"R")+(i%6+1)+" "+status;return labels;}
        internal static string ZoomText(int index) {return index>=0 && index<Zoom.Length ? Zoom[index] : "ZOOM --";}
        internal static string TubeText(int index,bool loaded) {return index>=0 && index<SalvoSequence.TubeCount ? (loaded ? Loaded[index] : Empty[index]) : "";}
        internal static string TubeText(int index,bool loaded,bool damaged)
        {return index>=0 && index<SalvoSequence.TubeCount ? (damaged ? Damaged[index] : TubeText(index,loaded)) : "";}
        internal static string TubeGlyph(bool loaded,bool damaged) {return damaged?"X":loaded?"R":"E";}
        internal static int PodFont(float height) {return FontSize(height);}
        internal static HudBox PodPanel(HudBox view)
        {
            float height=PodFont(view.H)*3.2f+Math.Max(2f,view.H*.006f)*3f;
            return new HudBox(view.X,view.Y+view.H-height,view.W,height);
        }
        internal static HudBox ScopeHeader(HudBox view,int item)
        {
            float pad=Math.Max(2f,view.H*.006f),font=PodFont(view.H),h=font*1.6f;
            HudBox panel=PodPanel(view);
            if(item==0)return new HudBox(view.X+pad,view.Y+view.H*.018f,view.W*.7f,h);
            if(item==1)return new HudBox(view.X+view.W-pad-h,view.Y+view.H*.018f,h,h);
            if(item==2)return new HudBox(view.X+pad,panel.Y+pad,view.W*.25f,h);
            if(item==3)return new HudBox(view.X+view.W*.75f,panel.Y+pad,view.W*.25f-pad,h);
            return new HudBox(view.X+pad,view.Y+view.H*.105f,view.W-2*pad,1f);
        }
        internal static HudBox PodItem(HudBox view,int side,int index)
        {
            float size=PodFont(view.H)*1.6f,gap=size*.15f,pad=Math.Max(2f,view.H*.006f),label=size;
            float width=label+size*6f+gap*5f;
            float left=side==0?view.X+pad:view.X+view.W-pad-width;
            float top=view.Y+view.H-pad-size;
            if(index<0)return new HudBox(left,top,label,size);
            return new HudBox(left+label+index*(size+gap),top,size,size);
        }
        internal static HudBox PodCenter(HudBox view,bool legend)
        {
            HudBox panel=PodPanel(view);float pad=Math.Max(2f,view.H*.006f);
            return new HudBox(view.X+view.W*.25f,panel.Y+pad,view.W*.5f,PodFont(view.H)*1.6f);
        }
        internal static int FontSize(float height) {return (int)Math.Max(12,Math.Min(28,Math.Round(height*16/1080)));}
        internal static HudBox Panel(HudBox view) {return new HudBox(view.X,view.Y+view.H*.76f,view.W,view.H*.24f);}
        internal static HudBox Picture(HudBox view) {return new HudBox(view.X,view.Y,view.W,view.H*.76f);}
        internal static HudBox ScanFrame(HudBox view)
        {
            HudBox picture=Picture(view);
            return new HudBox(picture.X+picture.W*.2f,picture.Y+picture.H*.18f,picture.W*.6f,picture.H*.64f);
        }
        internal static HudBox ScanFrame(HudBox view,int zoomIndex)
        {
            // Only the momentary E scan frame uses the 50x reference size.
            // This is presentation, not a narrower target-acquisition region.
            HudBox baseline=ScanFrame(view);
            float scale=SightFrameProjectionRules.ZoomScale(zoomIndex);
            float w=baseline.W*scale,h=baseline.H*scale;
            return new HudBox(baseline.X+(baseline.W-w)*.5f,baseline.Y+(baseline.H-h)*.5f,w,h);
        }
        internal static HudBox ScanFrame(HudBox view,int zoomIndex,float reticleX,float reticleY)
        {
            HudBox size=ScanFrame(view,zoomIndex);
            return new HudBox(reticleX-size.W*.5f,reticleY-size.H*.5f,size.W,size.H);
        }
        internal static HudBox Readout(HudBox view,int item)
        {
            HudBox panel=Panel(view);float font=FontSize(view.H);
            float width=Math.Min(panel.W*.9f,font*56f),left=panel.X+(panel.W-width)*.5f;
            float pad=Math.Min(10f,width*.012f),h=Math.Min(panel.H*.32f,font*1.7f);
            float top=panel.Y+Math.Min(panel.H*.1f,font*1.5f);
            if(item<2) return new HudBox(left+pad+(item==0 ? 0 : width*.30f),top,width*(item==0 ? .28f : .70f)-pad,h);
            float column=width/6f;int tube=item-2;
            return new HudBox(left+column*(tube%6)+pad,top+h*(1.25f+(tube/6)*1.2f),column-2*pad,h);
        }
        internal static string Status(bool ready,int direction,bool reloading,string reloadText,bool empty)
        {
            if(ready) return "Ready";
            if(reloading) return string.IsNullOrEmpty(reloadText) ? "Reloading" : reloadText;
            if(empty) return "Empty";
            // User's 'Out of range' means the fixed launcher's firing sector,
            // not busy guidance channels or a new range/eligibility restriction.
            if(direction!=0) return direction<0 ? "ALIGN LAUNCHER LEFT" : "ALIGN LAUNCHER RIGHT";
            return "Not ready";
        }
    }
}
