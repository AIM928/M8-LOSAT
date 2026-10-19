using System;
using System.Collections.Generic;
using GHPC;
using GHPC.Camera;
using GHPC.Effects;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private GameObject[] _tankImpactRoots;
        private ParticleSystem[][] _tankImpactParticles;
        private float[] _tankImpactUntil;
        private ParticleSystem.Burst[][][] _tankImpactBurstBuffers;
        private int _tankImpactSmokeLayers;
        private void InstallTankImpactSmokeHook()
        {
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiveRound),"penCheck"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(TankImpactSmokePrefix)));
        }
        private static void TankImpactSmokePrefix(LiveRound __instance,Collider __0,Vector3 __1,Vector3 __3)
        {
            LosatService owner=SalvoOwner;TrackedMissile t;
            if(owner==null || __instance==null || __0==null || __instance.IsSpall ||
                !owner._missileLookup.TryGetValue(__instance,out t) || t.RoundId!=__instance.ID || t.TankImpactShown) return;
            if(M8ContactRules.NativePassThrough(__0))return;
            IArmor armor=__0.GetComponent<IArmor>();
            Vehicle struck=__0.GetComponentInParent<Vehicle>();
            SalvoWeapon state;
            bool self=t.SourceWeapon!=null && SalvoWeapons.TryGetValue(t.SourceWeapon,out state) &&
                ((armor!=null && ReferenceEquals(armor.Unit,state.Vehicle.BaseUnit)) || struck==state.Vehicle);
            if(!ImpactSmokeRules.AcceptHit(true,__instance.IsSpall,t.TankImpactShown,
                struck!=null || (armor!=null && armor.Unit!=null),self)) return;
            t.TankImpactShown=true;
            // Exact native swept impactPoint, not a stale round position or HUD
            // target position. One cosmetic cloud, even through many armor layers.
            try {owner.PlayTankImpactSmoke(__3,__1);} catch(Exception e) {owner.WarnEffects(e);}
        }
        private void PrepareTankImpactSmoke(ImpactEffectsDatabaseScriptable database)
        {
            if(_tankImpactRoots!=null || database==null || _impactVisualAmmo==null || _impactVisualAmmo.CachedIndex<0) return;
            ImpactEffectDataScriptable data;
            if(!database.TryGetCachedImpactEffectData(_impactVisualAmmo,ParticleEffectsManager.FusedStatus.Unfuzed,
                ParticleEffectsManager.SurfaceMaterial.Dirt,false,out data) &&
                !database.TryGetCachedImpactEffectData(_impactVisualAmmo,ParticleEffectsManager.FusedStatus.Fuzed,
                ParticleEffectsManager.SurfaceMaterial.Dirt,false,out data)) return;
            if(data==null || data.EffectPrefab==null) return;
            var sources=new List<ParticleSystem>();ParticleSystem flash=null,primary=null,support=null;
            ParticleSystem[] candidates=data.EffectPrefab.GetComponentsInChildren<ParticleSystem>(true);
            int primaryScore=-1;
            foreach(ParticleSystem p in candidates)
            {
                if(!p.emission.enabled || p.main.maxParticles<=0) continue;
                int score=ImpactSmokeRules.SourceScore(p.name,true);
                if(score>primaryScore) {primary=p;primaryScore=score;}
                if(flash==null && ImpactSmokeRules.IsFlash(p.name)) flash=p;
            }
            if(primary==null) return;
            sources.Add(primary);int supportScore=-1;
            foreach(ParticleSystem p in candidates)
            {
                if(p==primary || !p.emission.enabled || p.main.maxParticles<=0) continue;
                int score=ImpactSmokeRules.SourceScore(p.name,false);
                if(score>supportScore) {support=p;supportScore=score;}
            }
            if(support!=null) sources.Add(support);
            _tankImpactSmokeLayers=sources.Count;
            if(flash!=null) sources.Add(flash);
            _tankImpactRoots=new GameObject[ImpactSmokeRules.PoolSize];
            _tankImpactParticles=new ParticleSystem[ImpactSmokeRules.PoolSize][];
            _tankImpactUntil=new float[ImpactSmokeRules.PoolSize];
            _tankImpactBurstBuffers=new ParticleSystem.Burst[ImpactSmokeRules.PoolSize][][];
            for(int slot=0;slot<_tankImpactRoots.Length;slot++)
            {
                var root=new GameObject("LOSAT_Cosmetic_Tank_Impact_"+slot);root.SetActive(false);_tankImpactRoots[slot]=root;
                var particles=new List<ParticleSystem>();
                _tankImpactBurstBuffers[slot]=new ParticleSystem.Burst[sources.Count][];
                foreach(ParticleSystem source in sources)
                {
                    // Private particle-only copies: no native explosion scripts,
                    // audio players, vision volumes or shared pool modifications.
                    ParticleSystem p=UnityEngine.Object.Instantiate(source,root.transform,false);
                    foreach(MonoBehaviour script in p.GetComponentsInChildren<MonoBehaviour>(true))
                    {script.enabled=false;UnityEngine.Object.Destroy(script);}
                    foreach(Collider collider in p.GetComponentsInChildren<Collider>(true))
                    {collider.enabled=false;UnityEngine.Object.Destroy(collider);}
                    foreach(ParticleSystem child in p.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        child.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                        if(child!=p) {var disabled=child.emission;disabled.enabled=false;}
                    }
                    bool isFlash=source==flash;var main=p.main;
                    main.playOnAwake=false;main.loop=false;main.prewarm=false;main.duration=.45f;main.startDelay=0f;
                    main.stopAction=ParticleSystemStopAction.None;main.simulationSpace=ParticleSystemSimulationSpace.World;
                    int layer=particles.Count;
                    main.startLifetimeMultiplier=ImpactSmokeRules.LayerLifetime(source.main.startLifetimeMultiplier,isFlash,layer);
                    main.startSizeMultiplier=ImpactSmokeRules.Size(source.main.startSizeMultiplier,isFlash);
                    main.maxParticles=isFlash ? 2 : Mathf.Clamp(source.main.maxParticles,1,ImpactSmokeRules.MaximumParticles);
                    var emission=p.emission;emission.enabled=true;emission.rateOverTime=0f;emission.rateOverDistance=0f;
                    var burst=new[] {new ParticleSystem.Burst(0f,(short)ImpactSmokeRules.BurstCount(main.maxParticles,isFlash,layer,0f))};
                    _tankImpactBurstBuffers[slot][layer]=burst;emission.SetBursts(burst);
                    var renderer=p.GetComponent<ParticleSystemRenderer>();
                    if(renderer!=null) {renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.velocityScale=0f;renderer.lengthScale=1f;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;}
                    p.transform.localPosition=Vector3.zero;p.transform.localRotation=Quaternion.identity;
                    p.gameObject.SetActive(true);particles.Add(p);
                }
                _tankImpactParticles[slot]=particles.ToArray();
            }
        }
        private void PlayTankImpactSmoke(Vector3 point,Vector3 normal)
        {
            if(_tankImpactRoots==null) return;
            int slot=ImpactSmokeRules.AvailableSlot(_tankImpactUntil,Time.time);
            if(slot<0) return; // Never instantiate or recycle a visible cloud on hit.
            GameObject root=_tankImpactRoots[slot];if(root==null) return;
            root.SetActive(false);
            root.transform.position=point+normal*.12f;
            root.transform.rotation=normal.sqrMagnitude>.01f ? Quaternion.LookRotation(normal) : Quaternion.identity;
            var camera=CameraManager.MainCam;
            float distanceSquared=camera==null ? 0f : ImpactSmokeRules.DetailDistanceSquared((point-camera.transform.position).sqrMagnitude,camera.fieldOfView);
            for(int i=0;i<_tankImpactParticles[slot].Length;i++)
            {
                ParticleSystem p=_tankImpactParticles[slot][i];p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                // Reuse preallocated burst arrays: no clones, materials, scene
                // searches or managed arrays on the impact path.
                var burst=_tankImpactBurstBuffers[slot][i];
                burst[0]=new ParticleSystem.Burst(0f,(short)ImpactSmokeRules.BurstCount(p.main.maxParticles,i>=_tankImpactSmokeLayers,i,distanceSquared));
                var emission=p.emission;emission.SetBursts(burst);
            }
            root.SetActive(true);
            foreach(ParticleSystem p in _tankImpactParticles[slot]) p.Play(false);
            _tankImpactUntil[slot]=Time.time+ImpactSmokeRules.LeaseSeconds;
        }
        private void ResetTankImpactSmoke()
        {
            if(_tankImpactRoots!=null) foreach(GameObject root in _tankImpactRoots) if(root!=null) UnityEngine.Object.Destroy(root);
            _tankImpactRoots=null;_tankImpactParticles=null;_tankImpactUntil=null;_tankImpactBurstBuffers=null;_tankImpactSmokeLayers=0;
        }
    }
}
