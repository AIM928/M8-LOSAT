using GHPC.Camera;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private GUIStyle _sightStatusStyle;
        private GUIStyle _scopeLabelStyle;
        private static readonly Color ScopeInkWhite=new Color(1f,1f,1f,1f);
        // M1-reference inspired pale-green brackets, crisp and independent of FLIR resolution.
        private static readonly Color SightFrameGreen=new Color(.62f,.94f,.50f,1f);
        private static void DrawSightCornerFrame(Rect r,float line)
        {
            float horizontal=Mathf.Clamp(r.width*.18f,6f,120f);
            float vertical=Mathf.Clamp(r.height*.18f,6f,64f);
            GUI.DrawTexture(new Rect(r.x,r.y,horizontal,line),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.y,line,vertical),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax-horizontal,r.y,horizontal,line),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax-line,r.y,line,vertical),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.yMax-line,horizontal,line),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x,r.yMax-vertical,line,vertical),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax-horizontal,r.yMax-line,horizontal,line),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax-line,r.yMax-vertical,line,vertical),Texture2D.whiteTexture);
        }
        private void DrawSightStatus(OpticsUpgrade sight,Rect view)
        {
            SalvoWeapon state;if(!SalvoWeapons.TryGetValue(sight.Weapon,out state)) return;
            HudBox area=new HudBox(view.x,view.y,view.width,view.height);
            Color old=GUI.color;
            // Restore the exact original centered text layout from the user's
            // screenshot. Tube labels alone are abbreviated; no cell boxes.
            GUI.color=Color.black;GUI.DrawTexture(GuiRect(SightStatusRules.Panel(area)),Texture2D.whiteTexture);
            if(_sightStatusStyle==null)
            {
                _sightStatusStyle=new GUIStyle(GUI.skin.label) {wordWrap=false,clipping=TextClipping.Clip,alignment=TextAnchor.MiddleLeft,fontStyle=FontStyle.Normal};
                _sightStatusStyle.normal.textColor=Color.white;
            }
            _sightStatusStyle.fontSize=SightStatusRules.FontSize(view.height);
            GUI.color=Color.white;
            CameraSlot slot=CameraSlot.ActiveInstance;
            int zoom=slot!=null && (slot==sight.DaySlot || slot==sight.ThermalSlot) ? slot.FovIndex : -1;
            GUI.Label(GuiRect(SightStatusRules.Readout(area,0)),SightStatusRules.ZoomText(zoom),_sightStatusStyle);
            bool empty=state.Weapon.Feed!=null && state.TubeLoad.Mask==0 && state.Weapon.Feed.ReserveCount==0;
            string status=SightStatusRules.Status(CanLaunchNow(sight),SectorDirection(sight),
                state.Weapon.Feed!=null && state.Weapon.Feed.Reloading,state.HudStatus,empty);
            if(M8SightDamaged(sight))status="F"; // Original lower status row, no long sentence.
            else if(empty && state.TubeLoad.DamagedMask!=0)status="NO USABLE TUBES";
            GUI.Label(GuiRect(SightStatusRules.Readout(area,1)),status,_sightStatusStyle);
            for(int i=0;i<SalvoSequence.TubeCount;i++)
                GUI.Label(GuiRect(SightStatusRules.Readout(area,i+2)),SightStatusRules.TubeText(i,state.TubeLoad.Loaded(i),state.TubeLoad.Damaged(i)),_sightStatusStyle);
            GUI.color=old;
        }
        private void DrawScopeLabel(HudBox box,string text,bool black,TextAnchor alignment)
        {
            GUIStyle style=black?_sightStatusStyle:_scopeLabelStyle;style.alignment=alignment;
            GUI.color=Color.white;GUI.Label(GuiRect(box),text,style);
        }
        private void DrawSightScanPulse(OpticsUpgrade sight,Rect view)
        {
            if(sight!=_sightFeedbackOwner || !_sightFeedback.PulseOn(Time.unscaledTime)) return;
            UnityEngine.Camera camera=CameraManager.MainCam;
            if(camera==null) return;
            Color old=GUI.color;GUI.color=SightFrameGreen;
            CameraSlot slot=CameraSlot.ActiveInstance;
            int zoom=slot!=null && (slot==sight.DaySlot || slot==sight.ThermalSlot) ? slot.FovIndex : -1;
            // Exact same raw-camera pixel center as DrawFixedSightLines, not
            // the picture center shifted upward by the bottom status panel.
            Rect pixels=camera.pixelRect;
            DrawSightCornerFrame(GuiRect(SightStatusRules.ScanFrame(new HudBox(view.x,view.y,view.width,view.height),zoom,
                pixels.center.x,Screen.height-pixels.center.y)),2f);
            GUI.color=old;
        }
    }
}
