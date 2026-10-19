using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private sealed class OpticsUpgrade
        {
            internal Vehicle Vehicle;
            internal WeaponSystem Weapon;
            internal CameraSlot DaySlot;
            internal CameraSlot ThermalSlot;
            internal int LastZoomIndex = -1;
            internal bool Installed;
            internal bool Failed;
            internal float NextAttempt;
            internal Renderer[] NativeReticleRenderers;
            internal NativeReticleVisibility[] NativeReticles;
            internal int LoggedReticleCount=-1,LoggedPostCount=-1;
            internal float NextReticleRefresh;
            internal bool ReticlesHidden;
            internal M8SightPair SightPair;
        }

        private readonly List<OpticsUpgrade> _opticUpgrades = new List<OpticsUpgrade>();
        private Material _thermalPaletteMaterial;

        private void QueueLOSATOptics(Vehicle vehicle, WeaponSystem weapon)
        {
            _opticUpgrades.Add(new OpticsUpgrade { Vehicle=vehicle, Weapon=weapon });
        }

        private void ResetLOSATOptics()
        {
            foreach(OpticsUpgrade state in _opticUpgrades) SetNativeReticlesHidden(state,false);
            foreach(OpticsUpgrade state in _opticUpgrades)if(state.SightPair!=null)state.SightPair.Dispose();
            M8ReticleDrawGate.Clear();
            if(_thermalPaletteMaterial != null) UnityEngine.Object.Destroy(_thermalPaletteMaterial);
            _thermalPaletteMaterial=null;
            _opticUpgrades.Clear();
        }

        private void UpdateLOSATOptics()
        {
            OpticsUpgrade activeSight=CurrentLockSight();
            foreach(OpticsUpgrade state in _opticUpgrades)
            {
                if(state.Vehicle == null || state.Weapon == null) continue;
                if(!state.Installed && !state.Failed && Time.unscaledTime >= state.NextAttempt)
                {
                    state.NextAttempt=Time.unscaledTime+1f;
                    try { state.Installed=TryInstallLOSATOptics(state); }
                    catch(Exception ex)
                    {
                        if(state.SightPair!=null){state.SightPair.Dispose();state.SightPair=null;}
                        state.Failed=true;
                        MelonLogger.Error(Prefix+"LOSAT optics upgrade failed safely: "+ex);
                    }
                }
                if(state.Installed)
                {
                    if(activeSight==state && Time.unscaledTime>=state.NextReticleRefresh)
                    {
                        state.NextReticleRefresh=Time.unscaledTime+.5f;
                        CacheNativeReticles(state,state.DaySlot.PairedOptic,state.ThermalSlot.PairedOptic);
                    }
                    // These are the converted vehicle's obsolete gun markings,
                    // not another unit's reticle. Keep them removed across scope
                    // switches/menu transitions; Restore is reserved for cleanup.
                    SetNativeReticlesHidden(state,true);
                    // Never recolor a daytime sight, the exterior camera or another vehicle.
                    bool ownSight=state.Installed && CameraSlot.ActiveInstance==state.ThermalSlot &&
                        GHPC.Player.PlayerInput.Instance != null &&
                        GHPC.Player.PlayerInput.Instance.CurrentPlayerUnit==state.Vehicle.BaseUnit;
                    if(ownSight && FLIRCamera.Instance != null && FLIRCamera.Instance.enabled)
                    {
                        if(state.LastZoomIndex != state.ThermalSlot.FovIndex)
                        {
                            state.LastZoomIndex=state.ThermalSlot.FovIndex;
                            ConfigureThermalResolution(state.ThermalSlot);
                            FLIRCamera.Instance.WidthOverride=state.ThermalSlot.FLIRWidth;
                            FLIRCamera.Instance.HeightOverride=state.ThermalSlot.FLIRHeight;
                        }
                    }
                }
            }
        }

        private bool TryInstallLOSATOptics(OpticsUpgrade state)
        {
            FireControlSystem fcs=state.Weapon.FCS;
            if(fcs == null || fcs.MainOptic == null || fcs.MainOptic.slot == null ||
                fcs.Mounts == null || fcs.Mounts.Length == 0 || FLIRCamera.Instance == null) return false;
            if(_thermalPaletteMaterial == null)
            {
                // Native M1 FLIR uses this exact asset (verified from its prefab).
                // Clone once; never alter the game's shared M1 display material.
                foreach(Material source in Resources.FindObjectsOfTypeAll<Material>())
                    if(source != null && source.name=="FLIR Green")
                    {
                        _thermalPaletteMaterial=new Material(source) {name="LOSAT_M1_FLIR_Green",hideFlags=HideFlags.HideAndDontSave};
                        break;
                    }
                // A cosmetic M1 palette must not gate M8 sight/reticle/control
                // installation when that unrelated asset is not resident.
            }
            UsableOptic dayOptic=SelectM8DayOptic(fcs);
            CameraSlot day=dayOptic.slot;
            if(day.VisionType != NightVisionType.None && day.LinkedDaySight != null)
            {
                day=day.LinkedDaySight;
                if(day.PairedOptic != null) dayOptic=day.PairedOptic;
            }

            fcs.SetStabsActive(false);
            fcs.CurrentStabMode=StabilizationMode.Vector;
            foreach(AimablePlatform mount in fcs.Mounts)
            {
                if(mount == null) continue;
                mount.Stabilized=true;
                mount.StabilizerActive=true;
            }
            fcs.SetStabsActive(true);
            // Retain native readiness, damage and angle gating, but remove the TOW's
            // stationary-only speed restriction for the newly stabilized mount.
            state.Weapon.MaxSpeedToFire=100f;
            state.Weapon.MaxSpeedToDeploy=100f;

            CameraSlot night=day.LinkedNightSight;
            if(night == null)
            {
                GameObject node=new GameObject("LOSAT_HD_Thermal_Sight");
                node.SetActive(false);
                node.layer=day.gameObject.layer;
                node.transform.SetParent(day.transform.parent,false);
                node.transform.localPosition=day.transform.localPosition;
                node.transform.localRotation=day.transform.localRotation;
                night=node.AddComponent<CameraSlot>();
                CopyPublicConfiguration(day,night);
                night.LinkedNightSight=null;
                night.ZeroScaleItems=new Transform[0];
                night.ExclusiveItems=new GameObject[0];
                night.ExclusiveWeapons=new[] {state.Weapon};
                night.AutoCenterTransform=node.transform;
                night.ZoomChangedEvents=new UnityEngine.Events.UnityEvent();
                night.ThermalPolarityChangedEvents=new UnityEngine.Events.UnityEvent<bool>();

                UsableOptic thermal=node.AddComponent<UsableOptic>();
                CopyPublicConfiguration(dayOptic,thermal);
                // Do not share daytime post volumes with the thermal camera: these
                // process encoded heat before the native compositor, not its display.
                thermal.post=null;thermal.AltNightPost=null;
                thermal.postAdditional=new PostProcessVolume[0];
                thermal.Transform=node.transform;
                thermal.slot=night;
                thermal.FCS=fcs;
                thermal.RegisterAtStart=false; // Do not replace the original daytime main optic.
                if(dayOptic.reticleMesh != null)
                    thermal.reticleMesh=UnityEngine.Object.Instantiate(dayOptic.reticleMesh,node.transform,false);
                List<Reticle.ReticleMesh> extras=new List<Reticle.ReticleMesh>();
                var originalExtras=GetField(dayOptic,"AdditionalReticleMeshes") as Reticle.ReticleMesh[];
                if(originalExtras != null)
                    foreach(var reticle in originalExtras)
                        if(reticle != null) extras.Add(UnityEngine.Object.Instantiate(reticle,node.transform,false));
                RequiredSet(thermal,"AdditionalReticleMeshes",extras.ToArray());
                night.RegisterOptic(thermal);
                ConfigureThermalSlot(night);
                RequiredSet(night,"<IsLinkedNightSight>k__BackingField",true);
                RequiredSet(night,"<LinkedDaySight>k__BackingField",day);
                day.LinkedNightSight=night;
                day.NightSightAtNightOnly=false;
                night.SetUsableByWeapon(true);
                node.SetActive(true); // Awake runs only after the copied configuration is complete.
            }
            else ConfigureThermalSlot(night);
            if(night.PairedOptic!=null&&night.PairedOptic.FCS!=fcs)throw new InvalidOperationException("M8 thermal slot belongs to a different fire control system.");
            state.SightPair=M8SightPair.Apply(day,night);
            RemoveOldIntensifierPost(night);
            day.NightSightAtNightOnly=false;
            night.NightSightAtNightOnly=false;
            day.BaseBlur=0f;day.VibrationBlurScale=0f;day.VibrationShakeMultiplier=.05f;
            night.BaseBlur=0f;night.VibrationBlurScale=0f;night.VibrationShakeMultiplier=.05f;
            dayOptic.UseRotationForShake=false;
            if(night.PairedOptic != null) night.PairedOptic.UseRotationForShake=false;
            ConfigureZoom(day);
            ConfigureZoom(night);
            ConfigureThermalResolution(night);
            RequiredSet(night,"_flirPolarity",false);
            fcs.RegisterOptic(dayOptic);
            state.ThermalSlot=night;
            state.DaySlot=day;
            CacheNativeReticles(state,dayOptic,night.PairedOptic);
            if(CameraSlot.ActiveInstance==night)
            {
                AccessTools.Method(typeof(CameraSlot),"ThisActiveChanged").Invoke(night,new object[] {true});
                night.UpdateFLIRPolarity();
            }
            return true;
        }

        private void ConfigureThermalSlot(CameraSlot slot)
        {
            slot.VisionType=NightVisionType.Thermal;
            slot.OverrideFLIRResolution=true;
            ConfigureThermalResolution(slot);
            slot.FLIRFilterMode=FilterMode.Bilinear;
            slot.DoMechanicalFLIR=false;
            if(_thermalPaletteMaterial!=null)slot.FLIRBlitMaterialOverride=_thermalPaletteMaterial;
            slot.CanToggleFlirPolarity=true;
        }

        private static void CacheNativeReticles(OpticsUpgrade state,UsableOptic day,UsableOptic night)
        {
            List<Renderer> renderers=state.NativeReticleRenderers==null ? new List<Renderer>() : new List<Renderer>(state.NativeReticleRenderers);
            List<UsableOptic> optics=new List<UsableOptic>();
            // Include slots moved outside FCS by native CameraSlot.Awake, both
            // initially and on refresh after a delayed reticle mesh is created.
            optics.AddRange(FindM8OwnedOptics(state.Weapon.FCS));
            foreach(CameraSlot slot in new[] {state.DaySlot,state.ThermalSlot})
                if(slot!=null)
                    foreach(UsableOptic optic in slot.GetComponentsInChildren<UsableOptic>(true))
                        if(!optics.Contains(optic)) optics.Add(optic);
            if(day!=null && !optics.Contains(day)) optics.Add(day);
            if(night!=null && !optics.Contains(night)) optics.Add(night);
            foreach(UsableOptic optic in optics)
            {
                if(optic==null) continue;
                List<Reticle.ReticleMesh> meshes=new List<Reticle.ReticleMesh>();
                // FLIR has zoom-exclusive TOW and WFOV ReticleMesh siblings
                // that are NOT in reticleMesh or AdditionalReticleMeshes.
                foreach(var descendant in optic.GetComponentsInChildren<Reticle.ReticleMesh>(true))
                    if(descendant!=null&&!meshes.Contains(descendant))meshes.Add(descendant);
                if(optic.reticleMesh!=null) meshes.Add(optic.reticleMesh);
                var extras=GetField(optic,"AdditionalReticleMeshes") as Reticle.ReticleMesh[];
                if(extras!=null) meshes.AddRange(extras);
                foreach(Reticle.ReticleMesh mesh in meshes)
                    if(mesh!=null)
                    {
                        M8ReticleDrawGate.AddRoot(mesh.transform);
                        foreach(Renderer renderer in mesh.GetComponentsInChildren<Renderer>(true))
                            if(!renderers.Contains(renderer)) renderers.Add(renderer);
                    }
                // Native range text is a separate TMP renderer, not necessarily
                // a child of ReticleMesh. It belongs to the removed gun reticle.
                Component rangeText=GetField(optic,"RangeText") as Component;
                if(rangeText!=null)
                {
                    M8ReticleDrawGate.AddRoot(rangeText.transform);
                    foreach(Renderer renderer in rangeText.GetComponentsInChildren<Renderer>(true))
                        if(!renderers.Contains(renderer))renderers.Add(renderer);
                }
            }
            int oldCount=state.NativeReticleRenderers==null ? 0 : state.NativeReticleRenderers.Length;
            if(renderers.Count!=oldCount)
            {
                state.NativeReticleRenderers=renderers.ToArray();
                if(state.NativeReticles==null) state.NativeReticles=new NativeReticleVisibility[0];
                Array.Resize(ref state.NativeReticles,renderers.Count);
                for(int i=oldCount;i<renderers.Count;i++)
                    state.NativeReticles[i]=new NativeReticleVisibility(renderers[i]);
            }
            if(state.NativeReticles==null) return;
            int postCount=0;
            foreach(NativeReticleVisibility reticle in state.NativeReticles)
            {
                reticle.RefreshPostProcessor();
                if(reticle.HasPostProcessor) postCount++;
                if(state.ReticlesHidden) reticle.Hide();
            }
            if(state.LoggedReticleCount!=renderers.Count || state.LoggedPostCount!=postCount)
            {
                state.LoggedReticleCount=renderers.Count;state.LoggedPostCount=postCount;
            }
        }

        private static void SetNativeReticlesHidden(OpticsUpgrade state,bool hidden)
        {
            if(state.NativeReticles==null || (!hidden && !state.ReticlesHidden)) return;
            foreach(NativeReticleVisibility reticle in state.NativeReticles)
                if(hidden) reticle.Hide(); else reticle.Restore();
            state.ReticlesHidden=hidden;
        }

        private void MaintainNativeReticles()
        {
            foreach(OpticsUpgrade state in _opticUpgrades)
                if(state.Installed&&state.Vehicle!=null)SetNativeReticlesHidden(state,true);
        }

        private static void DrawFixedSightLines(OpticsUpgrade sight)
        {
            if(!sight.ReticlesHidden) return; // Never draw over an unhided native crosshair.
            UnityEngine.Camera camera=CameraManager.MainCam;
            if(camera==null) return;
            Rect view=camera.pixelRect;
            float cx=view.center.x,cy=Screen.height-view.center.y;
            // Pixel-space geometry: no FOV, zoom index, world scale or FLIR size.
            float gap=3f,horizontal=12f,vertical=12f;
            const float thickness=1f;
            Color old=GUI.color;GUI.color=new Color(1f,1f,1f,.75f);
            GUI.DrawTexture(new Rect(cx-horizontal,cy-thickness*.5f,horizontal-gap,thickness),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx+gap,cy-thickness*.5f,horizontal-gap,thickness),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx-thickness*.5f,cy-vertical,thickness,vertical-gap),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx-thickness*.5f,cy+gap,thickness,vertical-gap),Texture2D.whiteTexture);
            GUI.color=old;
        }

        private static void ConfigureZoom(CameraSlot slot)
        {
            // Exact angular magnification relative to a 60-degree reference view.
            slot.DefaultFov=16.42642f; // 4x
            slot.OtherFovs=new[] {8.25562f,2.75611f,1.32313f}; // 8x,24x,50x
            slot.fovAspect=false;
            slot.AllowFreeZoom=false;
            slot.WrapFovLevels=false;
            RequiredSet(slot,"<FovIndex>k__BackingField",Mathf.Clamp(slot.FovIndex,0,3));
            if(CameraSlot.ActiveInstance==slot) slot.ForceUpdateFov();
        }

        private static void ConfigureThermalResolution(CameraSlot slot)
        {
            // Electronic zoom progressively loses sensor detail. HUD remains pixel sharp.
            slot.FLIRWidth=SectorPresentationRules.ThermalWidth(slot.FovIndex);
            slot.FLIRHeight=SectorPresentationRules.ThermalHeight(slot.FovIndex);
        }

        private static void RemoveOldIntensifierPost(CameraSlot slot)
        {
            foreach(SimpleNightVision intensifier in slot.GetComponentsInChildren<SimpleNightVision>(true))
            {
                if(intensifier.CameraSlot != slot) continue;
                var method=AccessTools.Method(typeof(SimpleNightVision),"CamSlotActiveChanged");
                var callback=(Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>),intensifier,method);
                slot.ActiveChanged-=callback;
                intensifier.enabled=false;
                var volume=GetField(intensifier,"_postVolume") as PostProcessVolume;
                if(volume == null) continue;
                volume.enabled=false;
                var optic=slot.PairedOptic;
                if(optic == null) continue;
                if(optic.post == volume) optic.post=null;
                if(optic.AltNightPost == volume) optic.AltNightPost=null;
                if(optic.postAdditional != null)
                {
                    List<PostProcessVolume> kept=new List<PostProcessVolume>();
                    foreach(var extra in optic.postAdditional) if(extra != volume) kept.Add(extra);
                    optic.postAdditional=kept.ToArray();
                }
            }
        }

        private static void CopyPublicConfiguration(object source,object target)
        {
            foreach(FieldInfo field in source.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public))
            {
                if(field.IsInitOnly || field.IsLiteral || typeof(Delegate).IsAssignableFrom(field.FieldType)) continue;
                if(field.Name=="FovLimitedItems" && field.FieldType.IsArray)
                {
                    field.SetValue(target,Array.CreateInstance(field.FieldType.GetElementType(),0));
                    continue;
                }
                field.SetValue(target,field.GetValue(source));
            }
        }
    }
}
