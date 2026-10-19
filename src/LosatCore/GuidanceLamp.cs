using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private Texture2D _guidanceLamp;
        private bool CanLaunchNow(OpticsUpgrade sight)
        {
            SalvoWeapon state;
            if(!LockInputReady() || sight==null || !SalvoWeapons.TryGetValue(sight.Weapon,out state) ||
                state.Guidance.Available==0 || state.TubeLoad.Mask==0 || state.Weapon.Feed==null || state.Weapon.Feed.CurrentClipRemainingCount==0 ||
                !state.Weapon.AbleToFire || state.Weapon.FCS==null || state.Weapon.FCS.FiringBlocked || _targetSalvo.Pending) return false;
            if(_targetLocks.Count==0) return ShotInLaunchSector(state,null);
            foreach(TargetLock target in _targetLocks)
                if(!target.Attack.InFlight && target.Visible && LockCandidateAlive(target.Vehicle) &&
                    TargetInLaunchSector(state,target.Vehicle.transform.TransformPoint(target.LocalAimPoint))) return true;
            return false;
        }
        private void DrawGuidanceLamp(OpticsUpgrade sight,Rect view)
        {
            if(_guidanceLamp==null)
            {
                _guidanceLamp=new Texture2D(16,16,TextureFormat.RGBA32,false);
                Color[] pixels=new Color[256];
                for(int y=0;y<16;y++) for(int x=0;x<16;x++)
                {float r=new Vector2(x-7.5f,y-7.5f).magnitude;pixels[y*16+x]=new Color(1f,1f,1f,Mathf.Clamp01(7.5f-r));}
                _guidanceLamp.SetPixels(pixels);_guidanceLamp.Apply();
                UnityEngine.Object.DontDestroyOnLoad(_guidanceLamp);
            }
            Color old=GUI.color;
            // Raised above the turn panel, moderately left of center; no
            // overlap even when the viewport is narrower than the panel.
            Rect lamp=new Rect(view.x+view.width*.30f-13f,Mathf.Max(view.y+80f,view.yMax-252f),26f,26f);
            GUI.color=new Color(.006f,.008f,.006f,.9f);GUI.DrawTexture(lamp,_guidanceLamp);
            if(GuidanceDisplayRules.LampOn(CanLaunchNow(sight),Time.unscaledTime))
            {GUI.color=new Color(.25f,1f,.18f,1f);GUI.DrawTexture(lamp,_guidanceLamp);}
            GUI.color=old;
        }
    }
}
