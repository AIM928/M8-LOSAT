using UnityEngine;
using GHPC.Weapons;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private static float BodyBearing(SalvoWeapon state,Vector3 direction)
        {
            M8Binding binding=SalvoOwner._m8Bindings.Find(b=>b.LauncherWeapon==state.Weapon);
            if(binding==null || binding.LauncherPivot==null) return float.PositiveInfinity;
            Vector3 local=binding.LauncherPivot.InverseTransformDirection(direction);
            return Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg;
        }
        private static bool TargetInLaunchSector(SalvoWeapon state,Vector3 point)
        {return FixedLauncherRules.Allowed(BodyBearing(state,point-state.Vehicle.transform.position));}
        private static Transform NativeSightAim(WeaponSystem weapon)
        {
            if(weapon.FCS!=null&&weapon.FCS.AimTransform!=null)return weapon.FCS.AimTransform;
            if(weapon.GuidanceUnit!=null && weapon.GuidanceUnit.AimElement!=null) return weapon.GuidanceUnit.AimElement;
            foreach(M8Binding swap in SalvoOwner._m8Bindings)
                if(swap.LauncherWeapon==weapon) return swap.LauncherAimReference;
            return weapon.transform;
        }
        private bool ShotInLaunchSector(SalvoWeapon state,GHPC.IUnit aiTarget)
        {
            if(_dispatchWeapon==state.Weapon && _dispatchTarget!=null)
                return TargetInLaunchSector(state,_dispatchTarget.Vehicle.transform.TransformPoint(_dispatchTarget.LocalAimPoint));
            GHPC.Vehicle.Vehicle target;
            if(aiTarget!=null && _lockVehicleByUnit.TryGetValue(aiTarget,out target) && LockCandidateAlive(target))
                return TargetInLaunchSector(state,target.transform.TransformPoint(CenterBodyBounds(target).center));
            Vector3 origin,direction;
            return GetManualSightRay(state.Weapon,out origin,out direction)&&FixedLauncherRules.Allowed(BodyBearing(state,direction));
        }
        private GUIStyle _sectorStyle;
        private int SectorDirection(OpticsUpgrade sight)
        {
            SalvoWeapon state;if(sight==null || !SalvoWeapons.TryGetValue(sight.Weapon,out state) || state.Vehicle==null) return 0;
            Vector3 origin,direction;if(!GetManualSightRay(state.Weapon,out origin,out direction))return 0;
            if(_selectedLock!=null && LockCandidateAlive(_selectedLock.Vehicle))
                direction=_selectedLock.Vehicle.transform.TransformPoint(_selectedLock.LocalAimPoint)-state.Vehicle.transform.position;
            return SectorPresentationRules.TurnDirection(BodyBearing(state,direction));
        }
        private void DrawLaunchSector(OpticsUpgrade sight,Rect view)
        {
            int direction=SectorDirection(sight);if(direction==0) return;
            if(_sectorStyle==null) _sectorStyle=new GUIStyle(_lockStyle) {fontSize=24,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
            Color old=GUI.color;
            Rect panel=new Rect(view.center.x-200f,view.yMax-132f,400f,84f);
            GUI.color=new Color(.01f,.025f,.012f,.72f);GUI.DrawTexture(panel,Texture2D.whiteTexture);
            GUI.color=new Color(1f,.78f,.24f,1f);
            GUI.Label(new Rect(panel.x,panel.y+2f,panel.width,32f),"OUT OF FIRING SECTOR",_sectorStyle);
            bool right=direction>0;
            GUI.Label(new Rect(panel.x+63f,panel.y+39f,274f,36f),right ? "TURN HULL RIGHT" : "TURN HULL LEFT",_sectorStyle);
            float x=right ? panel.x+panel.width-10f : panel.x+10f;
            float sign=right ? 1f : -1f,y=panel.y+56f;
            GuiLine(new Vector2(x-sign*50f,y),new Vector2(x,y),3f);
            GuiLine(new Vector2(x-sign*18f,y-15f),new Vector2(x,y),3f);
            GuiLine(new Vector2(x-sign*18f,y+15f),new Vector2(x,y),3f);
            GUI.color=old;
        }
    }
}
