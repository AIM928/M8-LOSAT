using GHPC.Camera;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private TargetLock _selectedLock;
        private static Texture2D _hudCircle;
        private static readonly System.Reflection.FieldInfo ClipFeedTimeField=AccessTools.Field(typeof(AmmoFeed),"_clipFeedTime");
        private readonly HudBox[] _lockRangeLabelBoxes=new HudBox[TargetLockRules.MaximumTargets];

        private static Rect GuiRect(HudBox box) {return new Rect(box.X,box.Y,box.W,box.H);}
        private static void GuiLine(Vector2 a,Vector2 b,float width)
        {
            Matrix4x4 saved=GUI.matrix;
            Vector2 delta=b-a;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,a);
            GUI.DrawTexture(new Rect(a.x,a.y-width*.5f,delta.magnitude,width),Texture2D.whiteTexture);
            GUI.matrix=saved;
        }
        private static void GuiBorder(Rect r,float width)
        {
            GUI.DrawTexture(new Rect(r.x,r.y,r.width,width),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.yMax-width,r.width,width),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.y,width,r.height),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax-width,r.y,width,r.height),Texture2D.whiteTexture);
        }
        private static Texture2D HudCircle()
        {
            if(_hudCircle!=null) return _hudCircle;
            const int size=64;
            Color32[] pixels=new Color32[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float d=Mathf.Sqrt((x-31.5f)*(x-31.5f)+(y-31.5f)*(y-31.5f));
                byte alpha=(byte)(Mathf.Clamp01(1f-Mathf.Abs(d-29f)/2f)*255);
                pixels[y*size+x]=new Color32(255,255,255,alpha);
            }
            _hudCircle=new Texture2D(size,size,TextureFormat.RGBA32,false);
            _hudCircle.name="LOSAT_HUD_Cached_Four_Tube_Ring";
            _hudCircle.SetPixels32(pixels);_hudCircle.Apply(false,true);
            _hudCircle.filterMode=FilterMode.Bilinear;_hudCircle.wrapMode=TextureWrapMode.Clamp;
            UnityEngine.Object.DontDestroyOnLoad(_hudCircle);return _hudCircle;
        }
        private void DrawTubeHud(OpticsUpgrade sight,Rect view)
        {
            DrawSightStatus(sight,view);
        }
        private static void RefreshTubeHud(SalvoWeapon state)
        {
            if(!IsPlayerWeapon(state) || Time.unscaledTime<state.NextHudUpdate) return;
            state.NextHudUpdate=Time.unscaledTime+.1f;
            AmmoFeed feed=state.Weapon.Feed;
            if(feed==null) return;
            if(feed.Reloading && ClipFeedTimeField!=null)
            {
                int seconds=TubeLoadState.Remaining(feed.TotalReloadTime,(float)ClipFeedTimeField.GetValue(feed));
                if(!state.HudWasReloading || seconds!=state.HudSeconds)
                {
                    state.HudStatus="RELOAD "+seconds+"s";
                    state.HudSeconds=seconds;
                }
                state.HudWasReloading=true;
            }
            else
            {
                state.HudWasReloading=false;
                state.HudStatus=state.TubeLoad.Mask==0 && feed.ReserveCount==0 ? "EMPTY" : "";
            }
        }
        private void DrawLockHud(Rect view)
        {
            int labels=0;
            HudBox picture=SightStatusRules.Picture(new HudBox(view.x,view.y,view.width,view.height));
            for(int i=0;i<_targetLocks.Count;i++)
            {
                TargetLock target=_targetLocks[i];if(!RenderedFrameAvailable(target)) continue;
                Rect r=RenderedLockDisplayRect(target);
                if(!TargetLockRules.FrameOn(target.Reacquiring,Time.unscaledTime)) continue;
                bool selected=target==_selectedLock;
                GUI.color=SightFrameGreen;
                DrawSightCornerFrame(r,selected ? 3f : 2f);
                if(target.Visible && target.RangeMeters>=0 && !string.IsNullOrEmpty(target.Caption))
                {
                    // Display the existing cached range only; no new ranging,
                    // target scan or hidden-target coordinate update.
                    HudBox label=SightHudRules.Place(new HudBox(r.x,r.y,r.width,r.height),picture,_lockRangeLabelBoxes,labels,84f,20f,i);
                    _lockRangeLabelBoxes[labels++]=label;
                    _lockStyle.normal.textColor=SightFrameGreen;
                    GUI.color=Color.white;GUI.Label(GuiRect(label),target.Caption,_lockStyle);
                }
            }
        }
    }
}
