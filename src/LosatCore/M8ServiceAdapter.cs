using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using GHPC.Weapons;
using UnityEngine;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private sealed class M8Binding
        {
            internal Vehicle Vehicle;
            internal WeaponSystem LauncherWeapon;
            internal Transform LauncherPivot,LauncherAimReference;
            internal Transform[] Muzzles;
            internal Action Synchronize;
            internal Action<int> TubeFired;
        }
        private readonly List<M8Binding> _m8Bindings=new List<M8Binding>();
        internal void SetTubeVisualHandler(WeaponSystem weapon,Action<int> handler)
        {var binding=_m8Bindings.Find(b=>b.LauncherWeapon==weapon);if(binding==null)throw new InvalidOperationException("M8 tube visual binding missing.");binding.TubeFired=handler;}
        private void HideFiredTube(WeaponSystem weapon,int index)
        {var binding=_m8Bindings.Find(b=>b.LauncherWeapon==weapon);if(binding!=null&&binding.TubeFired!=null)binding.TubeFired(index);}
        internal void MarkTubeLost(WeaponSystem weapon,int index)
        {
            SalvoWeapon state;if(weapon==null||!SalvoWeapons.TryGetValue(weapon,out state))return;
            state.TubeLoad.DamageTube(index);state.NextHudUpdate=0f;
            // Native WeaponSystem.Fire already cycles a destroyed barrel and wastes
            // exactly its one physical round, without muzzle FX or Fired callbacks.
            // Do not also dequeue here or one damaged missile would consume two.
        }
        internal bool Attach(Vehicle vehicle,WeaponSystem weapon,Transform turret,Transform[] muzzles,Action synchronize)
        {
            if(vehicle==null||weapon==null||turret==null||muzzles==null||muzzles.Length!=12||weapon.GuidanceUnit==null||weapon.GuidanceUnit.AimElement==null)
                throw new InvalidOperationException("M8 LOSAT weapon/sight/muzzle binding is incomplete.");
            if(SalvoWeapons.ContainsKey(weapon))return true;
            AmmoFeed feed=weapon.Feed;
            AmmoType source=feed.AmmoTypeInBreech??weapon.CurrentAmmoType??FirstAmmoType(feed.ReadyRack.ClipTypes);
            if(source==null||source.Guidance==AmmoType.GuidanceType.Unguided)throw new InvalidOperationException("Native M2 TOW data unavailable.");
            var binding=new M8Binding{Vehicle=vehicle,LauncherWeapon=weapon,LauncherPivot=turret,LauncherAimReference=weapon.GuidanceUnit.AimElement,Muzzles=muzzles,Synchronize=synchronize};
            _m8Bindings.Add(binding);
            try
            {
                AmmoType losat=CloneAmmoType(source);
                ConfigureKineticRound(losat,Mathf.Clamp(source.MuzzleVelocity,1f,MaximumSpeed));
                PrepareImpactPresentation(losat);
                var map=ReplaceRackClips(feed.ReadyRack,source,losat);
                ReplaceFeedState(feed,source,losat,map);
                SetField(weapon,"<CurrentAmmoType>k__BackingField",losat);
                ConfigureTwelveTubeSalvo(vehicle,weapon,losat);
                // User's rapid-fire audio trial: no sonic-boom allocation/playback.
                QueueLOSATOptics(vehicle,weapon);
                _patchedVehicles.Add(vehicle.GetInstanceID());
                return true;
            }
            catch{Detach(weapon);throw;}
        }
        internal void Detach(WeaponSystem weapon)
        {
            if(ReferenceEquals(weapon,null))return;
            SalvoWeapon state;
            if(SalvoWeapons.TryGetValue(weapon,out state))
            {
                if(weapon!=null && state.FireHandler!=null)weapon.Fired-=state.FireHandler;
                if(weapon!=null && weapon.Feed!=null && state.ClipHandler!=null)weapon.Feed.LoadedNewClip-=state.ClipHandler;
                SalvoWeapons.Remove(weapon);
                if(weapon!=null && weapon.Feed!=null)SalvoFeeds.Remove(weapon.Feed);
            }
            _m8Bindings.RemoveAll(b=>b.LauncherWeapon==weapon);
            for(int i=_opticUpgrades.Count-1;i>=0;i--)if(_opticUpgrades[i].Weapon==weapon){SetNativeReticlesHidden(_opticUpgrades[i],false);_opticUpgrades.RemoveAt(i);}
        }
        private void SynchronizeLauncher(WeaponSystem weapon)
        {foreach(var binding in _m8Bindings)if(binding.LauncherWeapon==weapon){if(binding.Synchronize!=null)binding.Synchronize();return;}}
        internal void Shutdown()
        {
            UnityEngine.Camera.onPostRender-=CaptureSightFrames;
            OnSceneWasLoaded(-1,"shutdown");
            HarmonyInstance.UnpatchSelf();
            SalvoOwner=null;
        }
    }
}
