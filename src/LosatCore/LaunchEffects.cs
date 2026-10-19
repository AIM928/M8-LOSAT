using System;
using System.Collections.Generic;
using GHPC.Effects;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private sealed class TowSmokeDefaults
        {
            internal ParticleSystem P;
            internal float Size,Life,Rate,Distance;
            internal int Maximum;
            internal bool EmissionEnabled,RendererEnabled;
            internal ParticleSystem.MinMaxCurve TimeCurve,DistanceCurve;
            internal ParticleSystem.MinMaxGradient StartColor;
            internal ParticleSystemRenderer Renderer;
            internal ParticleSystemRenderMode RenderMode;
            internal float VelocityScale,LengthScale;
        }
        private readonly Dictionary<ParticleSystem,TowSmokeDefaults> _towSmokeDefaults=new Dictionary<ParticleSystem,TowSmokeDefaults>();
        private readonly List<GameObject> _towMuzzleRoots=new List<GameObject>();
        private AmmoType _impactVisualAmmo;
        private bool _effectsWarned;
        private void InstallLaunchEffectsHook()
        {
            InstallTankImpactSmokeHook();
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiveRound),"doImpactVFX"),
                postfix:new HarmonyMethod(typeof(LosatService),nameof(LosatImpactPostfix)));
        }
        private static void LosatImpactPostfix(LiveRound __instance,bool __0)
        {
            LosatService o=SalvoOwner;TrackedMissile t;
            if(o==null || __instance==null || __instance.IsSpall ||
                !o._missileLookup.TryGetValue(__instance,out t) || t.RoundId!=__instance.ID || t.ImpactShown) return;
            t.ImpactShown=true;
            o.StopMissileMotor(__instance);
            // Native doImpactVFX already played the prepared visual. Never add
            // a second HE pool checkout (and audio/particles) on the same hit.
        }
        private void WarnEffects(Exception e)
        {if(!_effectsWarned) {_effectsWarned=true;MelonLogger.Warning(Prefix+"LOSAT visual effect fallback: "+e.Message);}}
        private bool EnlargeTowSmoke(ParticleSystem p,bool tail)
        {
            if(p==null) return false;
            TowSmokeDefaults d;
            if(!_towSmokeDefaults.TryGetValue(p,out d))
            {
                var m=p.main;var e=p.emission;
                d=new TowSmokeDefaults {P=p,Size=m.startSizeMultiplier,Life=m.startLifetimeMultiplier,
                    Maximum=m.maxParticles,Rate=e.rateOverTimeMultiplier,Distance=e.rateOverDistanceMultiplier,StartColor=m.startColor,
                    EmissionEnabled=e.enabled,TimeCurve=e.rateOverTime,DistanceCurve=e.rateOverDistance,
                    Renderer=p.GetComponent<ParticleSystemRenderer>()};
                if(d.Renderer!=null)
                {
                    d.RendererEnabled=d.Renderer.enabled;d.RenderMode=d.Renderer.renderMode;
                    d.VelocityScale=d.Renderer.velocityScale;d.LengthScale=d.Renderer.lengthScale;
                }
                _towSmokeDefaults.Add(p,d);
            }
            var emission=p.emission;
            if(!d.EmissionEnabled || (tail && p.name.IndexOf("Heat Distortion",StringComparison.OrdinalIgnoreCase)>=0))
            {
                // Stock TOW has a dormant 300 particles/sec smoke layer. Do not
                // wake it, or the high-speed round becomes an opaque white wall.
                emission.enabled=false;p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(d.Renderer!=null) d.Renderer.enabled=false;
                return false;
            }
            if(tail)
            {
                if(!TowSmokeRules.IsTail(p.name,d.EmissionEnabled)) return false;
                var tailMain=p.main;
                tailMain.startSizeMultiplier=TowSmokeRules.Size(d.Size);
                tailMain.startLifetimeMultiplier=TowSmokeRules.Lifetime(d.Life);
                // Native primary smoke starts at only 2–5% alpha. Retain its
                // lifetime fade/texture, but give this round a visible start tint.
                tailMain.startColor=new Color(.88f,.90f,.91f,TowSmokeRules.StartAlpha(p.name));
                tailMain.maxParticles=TowSmokeRules.TailMaxParticles;
                emission.enabled=true;
                emission.rateOverTime=new ParticleSystem.MinMaxCurve(0f);
                emission.rateOverDistance=new ParticleSystem.MinMaxCurve(TowSmokeRules.DistanceRate(p.name));
                if(d.Renderer!=null)
                {
                    // Native Stretch uses velocityScale=1. At 1500 m/s it draws
                    // long white ribbons. Use the same soft native texture as
                    // discrete billboards, independent of missile velocity.
                    d.Renderer.renderMode=ParticleSystemRenderMode.Billboard;
                    d.Renderer.velocityScale=0f;d.Renderer.lengthScale=1f;
                    d.Renderer.enabled=true; // Native pool may snapshot a temporarily hidden renderer.
                }
                return true;
            }
            // Scale the original curves, gradients, texture animation and shape:
            // no generated substitute, and no multiplicative growth on pool reuse.
            var main=p.main;main.startSizeMultiplier=TowSmokeRules.MuzzleSize(d.Size);
            main.startLifetimeMultiplier=TowSmokeRules.MuzzleLifetime(d.Life);
            main.maxParticles=Mathf.Clamp(Mathf.RoundToInt(d.Maximum*1.4f),32,512);
            emission.enabled=d.EmissionEnabled;
            emission.rateOverTimeMultiplier=d.Rate*1.15f;
            emission.rateOverDistanceMultiplier=d.Distance;
            if(d.Renderer!=null) d.Renderer.enabled=d.RendererEnabled;
            return true;
        }
        private ParticleSystem[] BuildNativeTowMuzzleEffects(WeaponSystem weapon,Transform muzzle)
        {
            var result=new List<ParticleSystem>();
            try
            {
                GameObject[] prefabs=weapon.MuzzleEffectPrefabs;
                if(prefabs!=null)
                    foreach(GameObject prefab in prefabs)
                    {
                        if(prefab==null) continue;
                        GameObject root=UnityEngine.Object.Instantiate(prefab,muzzle,false);
                        root.name="LOSAT_Original_TOW_MuzzleSmoke";
                        root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;
                        _towMuzzleRoots.Add(root);
                        foreach(ParticleSystem p in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                            EnlargeTowSmoke(p,false);result.Add(p);
                        }
                    }
                if(result.Count==0)
                {
                    // Stock M151 TOW stores attached systems rather than prefab
                    // references. Clone each original system once for this tube.
                    ParticleSystem[] attached=GetField(weapon,"_muzzleEffects") as ParticleSystem[];
                    if(attached!=null)
                        foreach(ParticleSystem source in attached)
                        {
                            if(source==null) continue;
                            ParticleSystem p=UnityEngine.Object.Instantiate(source,muzzle,false);
                            p.gameObject.name="LOSAT_Attached_Original_TOW_Smoke";
                            p.transform.localPosition=source.name.IndexOf("Backblast",StringComparison.OrdinalIgnoreCase)>=0 ? new Vector3(0f,0f,-2.25f) : Vector3.zero;
                            p.transform.localRotation=source.transform.localRotation;
                            _towMuzzleRoots.Add(p.gameObject);
                            foreach(ParticleSystem child in p.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                child.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                                EnlargeTowSmoke(child,false);result.Add(child);
                            }
                        }
                }
                // Native Fire plays these at the actual selected barrel. No
                // duplicate custom puff, per-shot cloning or per-frame scans.
            }
            catch(Exception e) {WarnEffects(e);}
            return result.ToArray();
        }
        private void RestoreNativeTowSmoke(LiveRound round)
        {
            foreach(ParticleSystem p in round.GetComponentsInChildren<ParticleSystem>(true))
            {
                if(p.gameObject.name=="LOSAT_MotorFlame") continue;
                if(!EnlargeTowSmoke(p,true)) continue;
                // Native pooling may hide the tracer visual parent. Reactivate
                // only this round's particle ancestry, then hide beam renderers.
                for(Transform node=p.transform;node!=null && node!=round.transform;node=node.parent)
                    if(!node.gameObject.activeSelf) node.gameObject.SetActive(true);
                p.Play(false);
            }
            RemoveMissileBeam(round);
        }
        private void LaunchPresentation(SalvoWeapon state,LiveRound round,Vector3 muzzle)
        {
            PlayLaunchAudio(state.Weapon,muzzle);
            try {ShowMissileMotor(round);} catch(Exception e) {WarnEffects(e);}
        }
        private void PrepareImpactPresentation(AmmoType ammo)
        {
            var manager=ParticleEffectsManager.Instance;if(manager==null || ammo==null) return;
            try
            {
                if(_impactVisualAmmo==null)
                {
                _impactVisualAmmo=CloneAmmoType(ammo);
                _impactVisualAmmo.Name="LOSAT cosmetic impact ONLY";_impactVisualAmmo.CachedIndex=-1;
                _impactVisualAmmo.ImpactEffectDescriptor=new ParticleEffectsManager.ImpactEffectDescriptor {
                    HasImpactEffect=true,ImpactCategory=ParticleEffectsManager.Category.HighExplosive,
                    EffectSize=ParticleEffectsManager.EffectSize.MainGun,
                    MinFilterStrictness=ParticleEffectsManager.FilterStrictness.Low};
                }
                // Cache the descriptor during setup, not in the impact hot path.
                var database=GetField(manager,"_impactEffectsScriptable") as ImpactEffectsDatabaseScriptable;
                if(database==null) return;
                if(_impactVisualAmmo.CachedIndex<0)
                {int index;database.CacheNewData(_impactVisualAmmo,out index);}
                ammo.ImpactEffectDescriptor=_impactVisualAmmo.ImpactEffectDescriptor;
                ammo.CachedIndex=_impactVisualAmmo.CachedIndex;
                PrepareTankImpactSmoke(database);
            }
            catch(Exception e) {WarnEffects(e);} // Keep native visuals on unavailable cache.
        }
        private void ResetLaunchEffects()
        {
            ResetMissileMotors();
            ResetTankImpactSmoke();
            foreach(TowSmokeDefaults d in _towSmokeDefaults.Values)
                if(d.P!=null)
                {
                    var m=d.P.main;m.startSizeMultiplier=d.Size;m.startLifetimeMultiplier=d.Life;m.maxParticles=d.Maximum;m.startColor=d.StartColor;
                    var e=d.P.emission;e.enabled=d.EmissionEnabled;
                    e.rateOverTime=d.TimeCurve;e.rateOverDistance=d.DistanceCurve;
                    e.rateOverTimeMultiplier=d.Rate;e.rateOverDistanceMultiplier=d.Distance;
                    if(d.Renderer!=null)
                    {
                        d.Renderer.renderMode=d.RenderMode;d.Renderer.velocityScale=d.VelocityScale;
                        d.Renderer.lengthScale=d.LengthScale;d.Renderer.enabled=d.RendererEnabled;
                    }
                }
            _towSmokeDefaults.Clear();
            foreach(GameObject root in _towMuzzleRoots) if(root!=null) UnityEngine.Object.Destroy(root);
            _towMuzzleRoots.Clear();_impactVisualAmmo=null;_effectsWarned=false;
        }
    }
}
