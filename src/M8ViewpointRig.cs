using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
namespace M8LOSATM2
{
    // Only camera-slot positions. Native parents, aiming/free-look rotations,
    // FCS references, field of view, damage gates and camera rendering stay native.
    internal sealed class M8ViewpointRig : IDisposable
    {
        // Glass center + 10 mm along its outward normal, from the actual mesh.
        internal static readonly Vector3 Gunner=new Vector3(0f,.6905923f,1.7501201f);
        // Center of the modeled commander cupola, 200 mm above its closed lid.
        internal static readonly Vector3 Commander=new Vector3(0f,1.4442479f,-.6322322f);
        private sealed class Saved
        {
            internal Transform Transform,Parent;internal Vector3 Before,Last,Target;internal bool Written;
        }
        private static readonly List<M8ViewpointRig> Active=new List<M8ViewpointRig>();
        // A global legacy BufferedCameraFollow also exists in this game build.
        // Qualify the current implementation: inherited private LateUpdate is
        // not found by reflecting the legacy wrapper's type.
        private static readonly MethodInfo CameraUpdate=typeof(GHPC.Camera.BufferedCameraFollow).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
        private static readonly MethodInfo Prefix=typeof(M8ViewpointRig).GetMethod("BeforeCameraLateUpdate",BindingFlags.Static|BindingFlags.NonPublic);
        private static HarmonyLib.Harmony _hook;
        private readonly List<Saved> _saved=new List<Saved>();
        private Transform _turret,_nativeTurret;private Vehicle _owner;private bool _disposed;
        private M8ViewLimits _limits;
        private static CameraSlot[] Select(Vehicle vehicle,FireControlSystem fcs)
        {
            if(vehicle==null||fcs==null)throw new InvalidOperationException("Missing M8 viewpoint owner/FCS.");
            var slots=vehicle.GetComponentsInChildren<CameraSlot>(true);CameraSlot day=null,commander=null,backup=null;
            foreach(var slot in slots)
            {
                if(slot==null||!slot.transform.IsChildOf(vehicle.transform))continue;
                var optic=slot.GetComponent<UsableOptic>();
                if(slot.name=="GPS Optic"&&slot.VisionType==NightVisionType.None&&optic!=null&&optic.FCS==fcs)
                {if(day!=null)throw new InvalidOperationException("Ambiguous M8 primary sight.");day=slot;}
                if(slot.name=="commander head"&&slot.AllowFreeLook&&slot.VisionType==NightVisionType.None)
                {if(commander!=null)throw new InvalidOperationException("Ambiguous M8 commander viewpoint.");commander=slot;}
            }
            if(day==null||commander==null||day.LinkedNightSight==null||commander.LinkedNightSight==null)
                throw new InvalidOperationException("Waiting for M8 primary and commander day/night slots.");
            var night=day.LinkedNightSight;var nods=commander.LinkedNightSight;
            if(night==day||nods==commander||night.VisionType!=NightVisionType.Thermal||
                !night.transform.IsChildOf(vehicle.transform)||!nods.transform.IsChildOf(vehicle.transform))
                throw new InvalidOperationException("M8 linked viewpoint belongs to a different vehicle.");
            var thermalOptic=night.GetComponent<UsableOptic>();
            if(thermalOptic==null||thermalOptic.FCS!=fcs)throw new InvalidOperationException("M8 thermal viewpoint FCS mismatch.");
            foreach(var slot in slots)if(slot!=null&&slot.name=="Back Up Sight"&&slot.LinkedNightSight==night&&slot.transform.IsChildOf(vehicle.transform))
            {if(backup!=null)throw new InvalidOperationException("Ambiguous M8 backup sight.");backup=slot;}
            return new[]{day,night,backup,commander,nods};
        }
        internal static bool IsReady(Vehicle vehicle,FireControlSystem fcs,out string reason)
        {
            try{if(CameraUpdate==null||Prefix==null)throw new InvalidOperationException("Native camera update unavailable.");Select(vehicle,fcs);reason="";return true;}
            catch(Exception e){reason=e.Message;return false;}
        }
        internal static M8ViewpointRig Apply(Vehicle vehicle,Transform turret,Transform nativeTurret,FireControlSystem fcs)
        {
            string reason;if(!IsReady(vehicle,fcs,out reason)||turret==null||!turret.IsChildOf(vehicle.transform)||nativeTurret==null||!nativeTurret.IsChildOf(vehicle.transform))
                throw new InvalidOperationException(reason.Length==0?"M8 viewpoint frame unavailable.":reason);
            foreach(var active in Active)if(active._owner==vehicle)throw new InvalidOperationException("M8 viewpoints already bound on this vehicle.");
            var rig=new M8ViewpointRig{_turret=turret,_nativeTurret=nativeTurret,_owner=vehicle};
            try
            {
                var slots=Select(vehicle,fcs);
                for(int i=0;i<slots.Length;i++)if(slots[i]!=null)
                {
                    Transform t=slots[i].transform;
                    foreach(var saved in rig._saved)if(saved.Transform==t)throw new InvalidOperationException("Duplicate M8 viewpoint transform.");
                    rig._saved.Add(new Saved{Transform=t,Parent=t.parent,Before=t.localPosition,Target=i<3?Gunner:Commander});
                }
                Active.Add(rig);
                if(_hook==null)
                {
                    var hook=new HarmonyLib.Harmony("M8LOSATM2.ViewpointPositions");
                    hook.Patch(CameraUpdate,prefix:new HarmonyMethod(Prefix){priority=Priority.Last});_hook=hook;
                }
                rig.Maintain();
                try{rig._limits=M8ViewLimits.Apply(vehicle,fcs,nativeTurret,slots[3],slots[4]);}
                catch(Exception e){MelonLogger.Warning("[M8 LOSAT] View limits unavailable; viewpoint positions retained: "+e.Message);}
                return rig;
            }
            catch{rig.Dispose();throw;}
        }
        internal void Maintain()
        {
            if(_disposed||_turret==null||_nativeTurret==null||_turret.parent==null)return;
            // BufferedCameraFollow may run before the model's late-sync pass.
            // Use this frame's native turret orientation without rotating the
            // visible model or relying on last frame's copied model rotation.
            Transform parent=_turret.parent;
            Quaternion relative=Quaternion.Inverse(parent.rotation)*_nativeTurret.rotation;
            foreach(var saved in _saved)
            {
                Transform t=saved.Transform;if(t==null||t.parent!=saved.Parent)continue;
                Vector3 desired=parent.TransformPoint(_turret.localPosition+relative*Vector3.Scale(saved.Target,_turret.localScale));
                if((t.position-desired).sqrMagnitude>.0000000001f)t.position=desired;
                saved.Last=t.localPosition;saved.Written=true;
            }
        }
        private static void BeforeCameraLateUpdate()
        {
            // Runs before native BufferedCameraFollow copies the active
            // slot. No main-camera edits, global discovery or forced slot switch.
            for(int i=Active.Count-1;i>=0;i--)
            {
                var rig=Active[i];
                try{rig.Maintain();}
                catch(Exception e){rig.Dispose();MelonLogger.Warning("[M8 LOSAT] Viewpoint positioning disabled safely: "+e.Message);}
            }
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;Active.Remove(this);
            if(_limits!=null)_limits.Dispose();
            for(int i=_saved.Count-1;i>=0;i--)
            {
                var s=_saved[i];if(s.Transform!=null&&s.Transform.parent==s.Parent&&s.Written&&
                    (s.Transform.localPosition-s.Last).sqrMagnitude<.00000001f)s.Transform.localPosition=s.Before;
            }
            _saved.Clear();
            if(Active.Count==0&&_hook!=null)
            {
                try{_hook.Unpatch(CameraUpdate,Prefix);}catch(Exception e){MelonLogger.Warning("[M8 LOSAT] Camera prefix cleanup: "+e.Message);}
                _hook=null;
            }
        }
    }
}
