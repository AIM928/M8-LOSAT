using GHPC.Camera;
using GHPC.Weapons;
using GHPC.Thermals;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private bool GetManualSightRay(WeaponSystem weapon,out Vector3 origin,out Vector3 direction)
        {
            origin=Vector3.zero;direction=Vector3.zero;
            // Same raw camera/viewport center as DrawFixedSightLines, including a
            // shifted projection. Never steer from another vehicle or free camera.
            // Reading an owned rendered sight ray must not depend on lock/HUD
            // input readiness. Native firing/menu transitions may gate those
            // flags while a manual missile is already in flight.
            OpticsUpgrade sight=CurrentLockViewContext();
            var camera=CameraManager.MainCam;
            if(sight!=null&&sight.Weapon==weapon&&camera!=null&&
                IsM8SightSlot(CameraSlot.ActiveInstance,sight.DaySlot,sight.ThermalSlot,weapon.FCS))
            {
                // The thermal image is rendered by a distinct native camera.
                // Use its actual projection, as the rendered target boxes do.
                if(CameraSlot.ActiveInstance==sight.ThermalSlot)
                {
                    var flir=FLIRCamera.Instance;
                    if(flir==null||!flir.enabled)return false;
                    camera=flir.GetComponent<Camera>();if(camera==null)return false;
                }
                Ray ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0f));
                origin=ray.origin;direction=ray.direction;
                return FiniteSight(origin)&&FiniteSight(direction)&&direction.sqrMagnitude>.5f;
            }
            Transform fallback=weapon!=null&&weapon.FCS!=null?weapon.FCS.AimTransform:null;
            origin=fallback==null?Vector3.zero:fallback.position;
            direction=fallback==null?Vector3.zero:fallback.forward;
            return fallback!=null&&FiniteSight(origin)&&FiniteSight(direction)&&direction.sqrMagnitude>.5f;
        }
        private static bool FiniteSight(Vector3 p)
        {return !float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);}
    }
}
