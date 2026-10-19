using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace M8LOSATM2
{
    // Hull-relative sight envelope, not an ammunition/flight-model change.
    internal sealed class M8ViewLimits : IDisposable
    {
        internal const float YawLimit=150f, MinimumPitch=-15f, MaximumPitch=30f;
        private static readonly List<M8ViewLimits> Active=new List<M8ViewLimits>();
        private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly MethodInfo[] Targets={
            typeof(FireControlSystem).GetMethod("set_AimWorldVector",Flags,null,new[]{typeof(Vector3)},null),
            typeof(FireControlSystem).GetMethod("FinalSetAimVector",Flags,null,new[]{typeof(Vector3),typeof(bool),typeof(bool)},null),
            typeof(FireControlSystem).GetMethod("LateUpdate",Flags,null,Type.EmptyTypes,null),
            typeof(GHPC.Camera.BufferedCameraFollow).GetMethod("TraverseBy",Flags,null,new[]{typeof(float)},null),
            typeof(GHPC.Camera.BufferedCameraFollow).GetMethod("ElevateBy",Flags,null,new[]{typeof(float)},null),
            typeof(GHPC.Camera.BufferedCameraFollow).GetMethod("UpdateCameraPosition",Flags,null,Type.EmptyTypes,null)};
        private static readonly string[] Callbacks={"ClampWorldAim","ClampFinalAim","BeforeFcsUpdate","AfterFreeLook","AfterFreeLook","BeforeCameraPosition"};
        private static HarmonyLib.Harmony _hook;
        private Vehicle _owner;private FireControlSystem _fcs;private AimablePlatform _traverse;
        private CameraSlot _commander,_nods;private Vector2 _before;private bool _disposed;

        internal static M8ViewLimits Apply(Vehicle owner,FireControlSystem fcs,Transform nativeTurret,CameraSlot commander,CameraSlot nods)
        {
            if(owner==null||fcs==null||nativeTurret==null||commander==null||nods==null||fcs.Mounts==null)
                throw new InvalidOperationException("Missing M8 camera-limit bindings.");
            AimablePlatform traverse=null;
            foreach(var mount in fcs.Mounts)if(mount!=null&&mount.Transform==nativeTurret&&(int)mount.Axis==1)
            {if(traverse!=null)throw new InvalidOperationException("Ambiguous M8 traverse mount.");traverse=mount;}
            if(traverse==null||!nativeTurret.IsChildOf(owner.transform))throw new InvalidOperationException("M8 native traverse mount unavailable.");
            foreach(var t in Targets)if(t==null)throw new MissingMethodException("Native M8 camera-limit API unavailable.");
            foreach(var other in Active)if(other._owner==owner)throw new InvalidOperationException("Duplicate M8 camera limits.");
            var rig=new M8ViewLimits{_owner=owner,_fcs=fcs,_traverse=traverse,_before=traverse.LocalEulerLimits,_commander=commander,_nods=nods};
            try
            {
                if(_hook==null)
                {
                    var h=new HarmonyLib.Harmony("M8LOSATM2.ViewLimits");
                    try
                    {
                        for(int i=0;i<Targets.Length;i++)
                        {
                            var callback=new HarmonyMethod(typeof(M8ViewLimits).GetMethod(Callbacks[i],BindingFlags.Static|BindingFlags.NonPublic)){priority=Priority.Last};
                            if(i==3||i==4)
                                h.Patch(Targets[i],prefix:new HarmonyMethod(typeof(M8ViewLimits).GetMethod(i==3?"BeforeTraverse":"BeforeElevate",BindingFlags.Static|BindingFlags.NonPublic)){priority=Priority.Last},postfix:callback);
                            else h.Patch(Targets[i],prefix:callback);
                        }
                    }
                    catch{Unpatch(h);throw;}
                    _hook=h;
                }
                traverse.LocalEulerLimits=new Vector2(-YawLimit,YawLimit);Active.Add(rig);return rig;
            }
            catch{rig.Dispose();throw;}
        }
        internal static Vector3 Bound(Vector3 local)
        {
            if(!Finite(local.x)||!Finite(local.y)||!Finite(local.z)||local.sqrMagnitude<.000000000001f)
                return new Vector3(0,0,1);
            float yaw=(float)(Math.Atan2(local.x,local.z)*180/Math.PI);
            float pitch=(float)(Math.Atan2(local.y,Math.Sqrt((double)local.x*local.x+(double)local.z*local.z))*180/Math.PI);
            if(yaw>=-YawLimit&&yaw<=YawLimit&&pitch>=MinimumPitch&&pitch<=MaximumPitch)return local;
            yaw=Mathf.Clamp(yaw,-YawLimit,YawLimit)*(float)Math.PI/180;
            pitch=Mathf.Clamp(pitch,MinimumPitch,MaximumPitch)*(float)Math.PI/180;
            float radius=local.magnitude,flat=(float)Math.Cos(pitch)*radius;
            return new Vector3((float)Math.Sin(yaw)*flat,(float)Math.Sin(pitch)*radius,(float)Math.Cos(yaw)*flat);
        }
        private static bool Finite(float f){return !float.IsNaN(f)&&!float.IsInfinity(f);}
        private bool PlayerOwns()
        {
            var p=GHPC.Player.PlayerInput.Instance;
            return !_disposed&&_owner!=null&&p!=null&&p.CurrentPlayerUnit==_owner.BaseUnit&&!GHPC.AarController.InAar&&!GHPC.State.TimeController.Paused;
        }
        private Vector3 WorldBound(Vector3 world)
        {
            Quaternion frame=_owner.transform.rotation;
            Vector3 local=Quaternion.Inverse(frame)*world,limited=Bound(local);
            return (limited-local).sqrMagnitude<.000000000001f?world:frame*limited;
        }
        private static void ClampWorldAim(FireControlSystem __instance,ref Vector3 __0)
        {foreach(var rig in Active)if(rig._fcs==__instance&&rig.PlayerOwns()){__0=rig.WorldBound(__0);return;}}
        private static void ClampFinalAim(FireControlSystem __instance,ref Vector3 __0,bool __1)
        {
            foreach(var rig in Active)if(rig._fcs==__instance&&rig.PlayerOwns())
            {
                if(__1)__0=rig.WorldBound(__0);
                else if(__instance.ReferenceTransform!=null)__0=__instance.ReferenceTransform.InverseTransformDirection(rig.WorldBound(__instance.ReferenceTransform.TransformDirection(__0)));
                return;
            }
        }
        private static void BeforeFcsUpdate(FireControlSystem __instance)
        {
            foreach(var rig in Active)if(rig._fcs==__instance&&rig.PlayerOwns())
            {
                Vector3 aim=__instance.AimWorldVector,limited=rig.WorldBound(aim);
                // Update the native demand as well as the displayed sight. No
                // accumulated over-travel behind a visually clamped crosshair.
                if((limited-aim).sqrMagnitude>.000000000001f)__instance.SetAimVector(limited,true);
                return;
            }
        }
        private static void AfterFreeLook(GHPC.Camera.BufferedCameraFollow __instance){LimitCommander(__instance);}
        private static void BeforeCameraPosition(GHPC.Camera.BufferedCameraFollow __instance){LimitCommander(__instance);}
        private static void BeforeTraverse(GHPC.Camera.BufferedCameraFollow __instance,ref float __0){LimitInput(__instance,ref __0,false);}
        private static void BeforeElevate(GHPC.Camera.BufferedCameraFollow __instance,ref float __0){LimitInput(__instance,ref __0,true);}
        private static void LimitInput(GHPC.Camera.BufferedCameraFollow camera,ref float amount,bool pitch)
        {
            if(camera==null||!camera.IsCommanderMode||camera.IsInFreeFlyMode||camera.BufferedCamera==null)return;
            var slot=CameraSlot.ActiveInstance;
            foreach(var rig in Active)if((slot==rig._commander||slot==rig._nods)&&rig.PlayerOwns())
            {
                LimitCommander(camera);
                Vector3 local=Quaternion.Inverse(rig._owner.transform.rotation)*camera.BufferedCamera.transform.forward;
                float angle=pitch?(float)(Math.Atan2(local.y,Math.Sqrt((double)local.x*local.x+(double)local.z*local.z))*180/Math.PI):(float)(Math.Atan2(local.x,local.z)*180/Math.PI);
                amount=Finite(amount)?Mathf.Clamp(amount,(pitch?MinimumPitch:-YawLimit)-angle,(pitch?MaximumPitch:YawLimit)-angle):0f;
                return;
            }
        }
        private static void LimitCommander(GHPC.Camera.BufferedCameraFollow camera)
        {
            if(camera==null||!camera.IsCommanderMode||camera.IsInFreeFlyMode||camera.BufferedCamera==null)return;
            var slot=CameraSlot.ActiveInstance;
            foreach(var rig in Active)if((slot==rig._commander||slot==rig._nods)&&rig.PlayerOwns())
            {
                Transform view=camera.BufferedCamera.transform;
                Vector3 current=view.forward,limited=rig.WorldBound(current);
                if((limited-current).sqrMagnitude>.000000000001f)
                {
                    Vector3 flat=new Vector3(limited.x,0,limited.z);
                    // Keep native yaw-root / pitch-child structure. No position,
                    // parent, FOV, sensitivity or global elevation-limit edits.
                    if(flat.sqrMagnitude>.00000001f)camera.transform.rotation=Quaternion.LookRotation(flat,Vector3.up);
                    view.rotation=Quaternion.LookRotation(limited,Vector3.up);
                }
                return;
            }
        }
        private static void Unpatch(HarmonyLib.Harmony h)
        {
            for(int i=0;i<Targets.Length;i++)if(Targets[i]!=null)
            {
                h.Unpatch(Targets[i],typeof(M8ViewLimits).GetMethod(Callbacks[i],BindingFlags.Static|BindingFlags.NonPublic));
                if(i==3||i==4)h.Unpatch(Targets[i],typeof(M8ViewLimits).GetMethod(i==3?"BeforeTraverse":"BeforeElevate",BindingFlags.Static|BindingFlags.NonPublic));
            }
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;Active.Remove(this);
            if(_traverse!=null&&_traverse.LocalEulerLimits.x==-YawLimit&&_traverse.LocalEulerLimits.y==YawLimit)_traverse.LocalEulerLimits=_before;
            if(Active.Count==0&&_hook!=null){try{Unpatch(_hook);}finally{_hook=null;}}
        }
    }
}
