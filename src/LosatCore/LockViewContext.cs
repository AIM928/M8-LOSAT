using System;
using GHPC;
using GHPC.Camera;
using GHPC.Player;
using HarmonyLib;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private bool _lockMirrorReturnActive;
        private CameraSlot _lockMirrorReturnSlot;
        private static readonly System.Reflection.FieldInfo PlayerCameraField=AccessTools.Field(typeof(PlayerInput),"_cam");
        private static void LockMirrorTogglePrefix(out bool __state)
        {
            LosatService o=SalvoOwner;
            __state=o!=null && o._lockOwner!=null && o.CurrentLockViewContext()==o._lockOwner;
        }
        private static void LockMirrorTogglePostfix(bool __state)
        {
            LosatService o=SalvoOwner;if(!__state || o==null || o._lockOwner==null) return;
            CameraSlot slot=CameraSlot.ActiveInstance;
            bool optic=IsM8SightSlot(slot,o._lockOwner.DaySlot,o._lockOwner.ThermalSlot,o._lockOwner.Weapon.FCS) ||
                (slot!=null && o._lockOwner.DaySlot!=null && slot.transform.IsChildOf(o._lockOwner.DaySlot.transform));
            // Closing may return to the last head slot, not just null. Remember
            // ONLY the slot reached by the real native mirror-toggle event.
            o._lockMirrorReturnActive=!optic;o._lockMirrorReturnSlot=optic ? null : slot;
        }
        private static void LockCrewViewCyclePrefix()
        {
            LosatService o=SalvoOwner;
            if(o!=null && o._lockOwner!=null && o.CurrentLockViewContext()==o._lockOwner) o.ClearTargetLock();
        }
        private OpticsUpgrade CurrentLockSight()
        {return LockViewOwner(true);}
        private OpticsUpgrade CurrentLockViewContext()
        {return LockViewOwner(false);}
        private static bool LockInputReady()
        {
            PlayerInput input=PlayerInput.Instance;
            return input!=null && input.SuspendInterfaceInputs && !input.SuspendGunnerInputs &&
                !input.SuspendCameraInputs && Time.timeScale>0f && !AarController.InAar;
        }
        private OpticsUpgrade LockViewOwner(bool requireOptic)
        {
            PlayerInput input=PlayerInput.Instance;
            // Avoid unsafe native AllowPlayer* getters: _cam can be null during
            // loading. SuspendInterfaceInputs is TRUE in live gameplay and
            // FALSE with the menu focused, despite its counterintuitive name.
            if(_opticUpgrades.Count==0 || input==null || input.CurrentPlayerUnit==null ||
                input.CurrentPlayerWeapon==null || AarController.InAar ||
                (requireOptic && !LockInputReady())) return null;
            CameraSlot slot=CameraSlot.ActiveInstance;
            if(PlayerCameraField==null || (requireOptic && (slot==null || CameraManager.MainCam==null))) return null;
            IPlayerCamera playerCamera=PlayerCameraField.GetValue(input) as IPlayerCamera;
            if(playerCamera==null) return null;
            UnityEngine.Object cameraObject=playerCamera as UnityEngine.Object;
            if(!ReferenceEquals(cameraObject,null) && cameraObject==null) return null;
            if(playerCamera.IsInFreeFlyMode) return null;
            foreach(OpticsUpgrade state in _opticUpgrades)
                if(state.Installed && state.Vehicle!=null && state.Weapon!=null &&
                    input.CurrentPlayerUnit==state.Vehicle.BaseUnit &&
                    input.CurrentPlayerWeapon!=null && input.CurrentPlayerWeapon.FCS==state.Weapon.FCS &&
                    LockViewRules.KeepOwner(requireOptic,slot!=null,
                        IsM8SightSlot(slot,state.DaySlot,state.ThermalSlot,state.Weapon.FCS),
                        slot!=null && state.DaySlot!=null && slot.transform.IsChildOf(state.DaySlot.transform),
                        _lockOwner==state && _lockMirrorReturnActive && slot==_lockMirrorReturnSlot)) return state;
            return null;
        }
    }
}
