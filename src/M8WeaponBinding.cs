using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Crew;
using GHPC.Vehicle;
using GHPC.Weapons;
using UnityEngine;
using M8LOSATM2.LosatCore;

namespace M8LOSATM2
{
    // Only this converted instance is changed. Keep native FCS/crew/damage nodes alive.
    internal sealed class M8WeaponBinding : IDisposable
    {
        private static readonly HashSet<WeaponSystem> Removed = new HashSet<WeaponSystem>();
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private sealed class SavedField
        {
            internal object Owner,Value; internal FieldInfo Field;
            internal void Restore(){if(!(Owner is UnityEngine.Object)|| (UnityEngine.Object)Owner!=null)Field.SetValue(Owner,Value);}
        }
        private readonly List<SavedField> _saved=new List<SavedField>();
        private readonly Dictionary<WeaponSystem,bool> _disabled=new Dictionary<WeaponSystem,bool>();
        private readonly Dictionary<AmmoFeed,bool> _disabledFeeds=new Dictionary<AmmoFeed,bool>();
        private readonly Dictionary<CrewBrainWeaponsModule,WeaponSystemInfo> _active=new Dictionary<CrewBrainWeaponsModule,WeaponSystemInfo>();
        private readonly List<GameObject> _muzzleObjects=new List<GameObject>();
        private Vehicle _vehicle; private WeaponSystem _weapon; private WeaponSystemInfo _info;
        private LosatService _service; private bool _disposed;
        internal static bool IsRemoved(WeaponSystem weapon){return !ReferenceEquals(weapon,null)&&Removed.Contains(weapon);}
        private void Save(object owner,params string[] names)
        {
            foreach(string name in names)
            {
                FieldInfo field=owner.GetType().GetField(name,Flags);
                if(field==null)throw new MissingFieldException(owner.GetType().FullName,name);
                _saved.Add(new SavedField{Owner=owner,Field=field,Value=field.GetValue(owner)});
            }
        }
        private static void Set(object owner,string name,object value)
        {FieldInfo field=owner.GetType().GetField(name,Flags);if(field==null)throw new MissingFieldException(name);field.SetValue(owner,value);}
        internal static WeaponSystem FindLauncher(Vehicle vehicle)
        {
            if(vehicle==null||vehicle.BaseUnit==null||vehicle.BaseUnit.WeaponsManager==null)return null;
            foreach(var info in vehicle.BaseUnit.WeaponsManager.Weapons)
            {
                if(info==null||info.Weapon==null)continue;
                var w=info.Weapon;
                if(w.GuidanceUnit!=null&&w.GuidanceUnit.AimElement!=null&&w.Feed!=null&&w.Feed.ReadyRack!=null&&w.FCS!=null)
                    return w;
            }
            return null;
        }
        internal static M8WeaponBinding Create(Vehicle vehicle,Transform turret,LosatService service,Action synchronize,Action<int> tubeFired=null)
        {
            var result=new M8WeaponBinding{_vehicle=vehicle,_service=service};
            try{result.Install(turret,synchronize,tubeFired);return result;}catch{result.Dispose();throw;}
        }
        private void Install(Transform turret,Action synchronize,Action<int> tubeFired)
        {
            _weapon=FindLauncher(_vehicle);
            if(_weapon==null)throw new InvalidOperationException("Native M2 guided weapon is not ready.");
            WeaponsManager manager=_vehicle.BaseUnit.WeaponsManager;
            WeaponSystemInfo original=Array.Find(manager.Weapons, i=>i!=null&&i.Weapon==_weapon);
            if(original==null)throw new InvalidOperationException("Native M2 launcher entry missing.");
            Save(manager,"Weapons");
            // Native menu and restock hotkey both honor NoRestocking. Keep the
            // component alive; do not alter startup ammunition or feed/reload.
            foreach(var loadout in _vehicle.GetComponentsInChildren<LoadoutManager>(true))
            {
                Save(loadout,"NoRestocking");
                if(loadout.IsRestocking)loadout.StopRestock();
                loadout.NoRestocking=true;
            }
            // Remove only this vehicle's smoke grenades and native menu/action
            // capability. Keep slot arrays intact for any native delayed salvo;
            // zero the private slot counts, never mutate a shared smoke prefab.
            foreach(var smoke in _vehicle.GetComponentsInChildren<GHPC.Equipment.VehicleSmokeManager>(true))
            {
                Save(smoke,"_hasSmokeLauncher");Set(smoke,"_hasSmokeLauncher",false);
                var slots=smoke.GetType().GetField("_smokeSlots",Flags).GetValue(smoke) as Array;
                if(slots!=null)foreach(var slot in slots)if(slot!=null){Save(slot,"Rounds");Set(slot,"Rounds",0);}
            }
            var fcs=_weapon.FCS;
            Save(fcs,"LinkedWeaponSystems","<CurrentWeaponSystem>k__BackingField","SuperelevateWeapon","SuperleadWeapon",
                "<MainOptic>k__BackingField","<NightOptic>k__BackingField","_originalMainOpticAlign");
            Save(_weapon,"MultiBarrels","ResetBarrelIndexOnRelease","_isMultiBarrel","_multiBarrelIndex",
                "FireWhileGuidingMissile","WireGuided","TriggerHoldTime","<CurrentAmmoType>k__BackingField",
                "_cycleTimeSeconds","_cycleTimeRemaining","_deployed","DeployAnimator","MaxSpeedToFire","MaxSpeedToDeploy");
            Save(_weapon.GuidanceUnit,"ResetAimOnLaunch");
            var feed=_weapon.Feed;var rack=feed.ReadyRack;
            Save(rack,"UseVisibleRounds","ClipCapacity","_clipTypes","<StoredClips>k__BackingField","_initialClipCounts");
            Save(feed,"AutoFeed","ReloadDuringMissileTracking","DualFeed","_auxFeedMode","_feedClipMain","_feedClipAux",
                "<LoadedClipType>k__BackingField","<QueuedClipType>k__BackingField","_queuedClipTypeLockedIn",
                "<AmmoTypeInBreech>k__BackingField","<WaitingOnMissile>k__BackingField","<Reloading>k__BackingField",
                "<Cycling>k__BackingField","RoundCycleStages","ClipReloadStages","_totalCycleTime","_totalReloadTime");
            // Core replacement routines mutate these containers: detach them first for reliable rollback.
            Set(rack,"<StoredClips>k__BackingField",new List<AmmoType.AmmoClip>(rack.StoredClips));
            foreach(string field in new[]{"_feedClipMain","_feedClipAux"})
            {var queue=typeof(AmmoFeed).GetField(field,Flags).GetValue(feed) as Queue<AmmoType>;Set(feed,field,queue==null?new Queue<AmmoType>():new Queue<AmmoType>(queue));}
            var muzzles=new Transform[12];
            for(int i=0;i<12;i++)
            {
                var node=new GameObject("M8_LOSAT_"+(i<6?"L":"R")+(i%6+1)+"_Muzzle");
                _muzzleObjects.Add(node);node.layer=turret.gameObject.layer;
                node.transform.SetParent(turret,false);node.transform.localPosition=M8Hardpoints.Positions[i];
                node.transform.localRotation=Quaternion.identity;muzzles[i]=node.transform;
            }
            Set(_weapon,"_deployed",true);_weapon.DeployAnimator=null;
            _weapon.MaxSpeedToFire=100f;_weapon.MaxSpeedToDeploy=100f;
            _service.Attach(_vehicle,_weapon,turret,muzzles,synchronize);
            _service.SetTubeVisualHandler(_weapon,tubeFired);
            _info=(WeaponSystemInfo)typeof(object).GetMethod("MemberwiseClone",Flags).Invoke(original,null);
            _info.Name="MGM-166 LOSAT";_info.Role=WeaponSystemRole.MainGun;
            _info.NeedsPreAim=false;_info.PreAimWeapon=WeaponSystemRole.MainGun;_info.NeedsManualRealignment=false;
            foreach(var info in manager.Weapons)
            {
                if(info==null||info.Weapon==null||info.Weapon==_weapon||_disabled.ContainsKey(info.Weapon))continue;
                var old=info.Weapon;_disabled.Add(old,old.enabled);Removed.Add(old);old.StopFiring();old.enabled=false;
                if(old.Feed!=null&&old.Feed!=_weapon.Feed&&!_disabledFeeds.ContainsKey(old.Feed))
                {_disabledFeeds.Add(old.Feed,old.Feed.enabled);old.Feed.enabled=false;}
            }
            fcs.LinkedWeaponSystems=new[]{_weapon};Set(fcs,"<CurrentWeaponSystem>k__BackingField",_weapon);
            fcs.SuperelevateWeapon=false;fcs.SuperleadWeapon=false;
            manager.Weapons=new[]{_info};
            var crew=_vehicle.BaseUnit.CrewManager;
            if(crew!=null)foreach(CrewPosition position in Enum.GetValues(typeof(CrewPosition)))
            {var brain=crew.GetCrewBrain(position) as CrewBrain;if(brain!=null)BindBrain(brain.WeaponsModule);}
            MaintainSelection();
            M8PenetrationBailPolicy.Register(_vehicle);
            try{M8CrewVoicePolicy.Register(_vehicle);}
            catch(Exception e){MelonLoader.MelonLogger.Warning(Mod.Prefix+"Crew voice filter unavailable: "+e.Message);}
        }
        private void BindBrain(CrewBrainWeaponsModule module)
        {
            if(module==null||_active.ContainsKey(module)||module.Weapons==null)return;
            bool own=false;foreach(var info in module.Weapons)if(info!=null&&info.Weapon!=null&&(info.Weapon==_weapon||_disabled.ContainsKey(info.Weapon))){own=true;break;}
            if(!own)return;
            Save(module,"<Weapons>k__BackingField");_active.Add(module,module.ActiveWeapon);
            Set(module,"<Weapons>k__BackingField",new List<WeaponSystemInfo>{_info});
            module.ActiveWeapon=_info;
        }
        internal void MaintainSelection()
        {
            var player=GHPC.Player.PlayerInput.Instance;
            if(player==null||_vehicle==null||player.CurrentPlayerUnit!=_vehicle.BaseUnit)return;
            var brain=player.CurrentPlayerCrewBrain as CrewBrain;
            if(brain==null)return;var module=brain.WeaponsModule;BindBrain(module);
            // Suspended brains ignore ActiveWeapon assignments. Retry only on player entry.
            if(module!=null&&_active.ContainsKey(module)&&module.ActiveWeapon!=_info)module.ActiveWeapon=_info;
        }
        internal void BindMissileDamage(GHPC.Equipment.DestructibleComponent[] damage)
        {
            if(damage==null||damage.Length!=12||_weapon.MultiBarrels.Length!=12)throw new InvalidOperationException("Twelve missile damage zones required.");
            for(int i=0;i<12;i++)
            {
                if(damage[i]==null)throw new InvalidOperationException("Missing missile damage zone.");
                _weapon.MultiBarrels[i].Destructible=damage[i];
            }
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            Exception error=null;
            try{M8PenetrationBailPolicy.Unregister(_vehicle);}catch(Exception e){error=e;}
            try{M8CrewVoicePolicy.Unregister(_vehicle);}catch(Exception e){error=e;}
            try{if(_service!=null)_service.Detach(_weapon);}catch(Exception e){error=e;}
            for(int i=_saved.Count-1;i>=0;i--)try{_saved[i].Restore();}catch(Exception e){error=e;}
            foreach(var pair in _active)try{pair.Key.ActiveWeapon=pair.Value;}catch(Exception e){error=e;}
            foreach(var pair in _disabled){Removed.Remove(pair.Key);if(pair.Key!=null)pair.Key.enabled=pair.Value;}
            foreach(var pair in _disabledFeeds)if(pair.Key!=null)pair.Key.enabled=pair.Value;
            foreach(var node in _muzzleObjects)if(node!=null)UnityEngine.Object.Destroy(node);
            _saved.Clear();_active.Clear();_disabled.Clear();_disabledFeeds.Clear();_muzzleObjects.Clear();
            if(error!=null)MelonLoader.MelonLogger.Warning(Mod.Prefix+"weapon cleanup on scene exit: "+error.GetBaseException().Message);
        }
    }
}
