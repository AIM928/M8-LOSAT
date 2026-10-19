using System;
using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private readonly List<Vehicle> _knownVehicles=new List<Vehicle>();
        private readonly HashSet<Vehicle> _knownVehicleSet=new HashSet<Vehicle>();
        private readonly Queue<Vehicle> _newVehicles=new Queue<Vehicle>();
        private Vehicle[] _vehicleSnapshot;
        private int _snapshotCursor,_knownCursor,_knownRemaining;
        private float _nextVehicleFallback,_nextCachedPass,_nextDiscoveryWarning;
        private bool _vehicleDiscoveryHooked;
        private void InstallDiscoveryHooks()
        {
            try {
                HarmonyInstance.Patch(AccessTools.Method(typeof(Vehicle),"Awake"),
                    postfix:new HarmonyMethod(typeof(LosatService),nameof(DiscoveryVehicleAwakePostfix)));
                _vehicleDiscoveryHooked=true;
            } catch(Exception ex) {MelonLogger.Warning(Prefix+"Vehicle discovery uses fallback: "+ex.Message);}
            try {
                HarmonyInstance.Patch(AccessTools.Method(typeof(UniformArmor),"Awake"),
                    postfix:new HarmonyMethod(typeof(LosatService),nameof(DiscoveryArmorAwakePostfix)));
            } catch(Exception ex) {MelonLogger.Warning(Prefix+"ERA discovery uses fallback: "+ex.Message);}
        }
        private static void DiscoveryVehicleAwakePostfix(Vehicle __instance)
        {LosatService owner=SalvoOwner;if(owner!=null) owner.RememberDiscoveryVehicle(__instance,true);}
        private static void DiscoveryArmorAwakePostfix(UniformArmor __instance)
        {LosatService owner=SalvoOwner;if(owner!=null && __instance!=null) owner.RememberEraArmor(__instance.ArmorType);}
        private void RememberDiscoveryVehicle(Vehicle vehicle,bool urgent)
        {
            if(vehicle==null || vehicle.gameObject==null || !vehicle.gameObject.scene.IsValid() || !_knownVehicleSet.Add(vehicle)) return;
            _knownVehicles.Add(vehicle);
            if(urgent) {
                _newVehicles.Enqueue(vehicle);
                // Some mods assign armor codices after native Awake. Coalesce
                // new-vehicle notifications into an earlier fallback, rather
                // than leaving those reinforcements waiting for the 30s sweep.
                _nextEraFallback=Math.Min(_nextEraFallback,Time.unscaledTime+DiscoveryBudget.CachedPassSeconds);
            }
        }
        private void ResetDiscovery()
        {
            _knownVehicles.Clear();_knownVehicleSet.Clear();_newVehicles.Clear();_vehicleSnapshot=null;
            _snapshotCursor=_knownCursor=_knownRemaining=0;
            _nextVehicleFallback=_nextCachedPass=_nextDiscoveryWarning=0f;
        }
        private bool CheckDiscoveryVehicle(Vehicle vehicle)
        {
            if(vehicle==null || vehicle.gameObject==null || !vehicle.gameObject.scene.IsValid()) return false;
            RememberLockCandidate(vehicle);
            if(_patchedVehicles.Contains(vehicle.GetInstanceID()) || !IsLiveTarget(vehicle)) return false;
            if(PatchVehicle(vehicle)) _patchedVehicles.Add(vehicle.GetInstanceID());
            return true; // At most one expensive LOSAT installation attempt per frame.
        }
        private void UpdateDiscovery()
        {
            float now=Time.unscaledTime;bool sceneScan=false;
            try {
                if(_vehicleSnapshot==null && now>=_nextVehicleFallback) {
                    _nextVehicleFallback=now+DiscoveryBudget.VehicleFallbackSeconds(now-_sceneStartTime,_vehicleDiscoveryHooked);
                    _vehicleSnapshot=Resources.FindObjectsOfTypeAll<Vehicle>();_snapshotCursor=0;sceneScan=true;
                }
                int budget=DiscoveryBudget.VehiclesPerFrame;
                // Feed a bounded part of the fallback snapshot; no full-array
                // hierarchy walk or per-scan destruction of collider caches.
                if(_vehicleSnapshot!=null) {
                    int take=Math.Min(budget,_vehicleSnapshot.Length-_snapshotCursor);
                    for(int i=0;i<take;i++) RememberDiscoveryVehicle(_vehicleSnapshot[_snapshotCursor++],true);
                    if(_snapshotCursor==_vehicleSnapshot.Length) _vehicleSnapshot=null;
                }
                if(_knownRemaining==0 && now>=_nextCachedPass) {
                    _nextCachedPass=now+DiscoveryBudget.CachedPassSeconds;
                    _knownCursor=0;_knownRemaining=_knownVehicles.Count;
                }
                while(budget-->0) {
                    Vehicle vehicle;
                    if(_newVehicles.Count>0) vehicle=_newVehicles.Dequeue();
                    else if(_knownRemaining>0) {
                        vehicle=_knownVehicles[_knownCursor++];_knownRemaining--;
                        if(vehicle==null) {
                            // Swap-remove dead entries inside the same bounded pass;
                            // keep inactive living vehicles for later activation.
                            _knownVehicleSet.Remove(vehicle);ForgetLockCandidate(vehicle);
                            int index=--_knownCursor,last=_knownVehicles.Count-1;
                            _knownVehicles[index]=_knownVehicles[last];_knownVehicles.RemoveAt(last);
                            _knownRemaining=Math.Min(_knownRemaining+1,_knownVehicles.Count-_knownCursor);
                            continue;
                        }
                    }
                    else break;
                    if(CheckDiscoveryVehicle(vehicle)) break;
                }
                RefreshEraArmorCatalog(!sceneScan);
            } catch(Exception ex) {
                // Keep a real diagnostic without repeating the same failure every frame.
                if(now>=_nextDiscoveryWarning) {_nextDiscoveryWarning=now+10f;MelonLogger.Error(Prefix+"discovery retry: "+ex);}
            }
        }
    }
}
