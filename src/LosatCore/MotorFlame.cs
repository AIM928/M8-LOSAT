using System;
using System.Collections.Generic;
using GHPC.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private sealed class MotorFlame
        {internal ParticleSystem Particles;internal int RoundId;}
        private readonly Dictionary<LiveRound,MotorFlame> _motorFlames=new Dictionary<LiveRound,MotorFlame>();
        private Material _motorFlameMaterial;
        private bool _motorMaterialMissing;
        private void ShowMissileMotor(LiveRound round)
        {
            if(round==null || round.Pooled || round.IsDestroyed || _motorMaterialMissing) return;
            if(_motorFlameMaterial==null)
            {
                // Reuse the game's soft additive flare texture; never change its
                // shared material. One lookup per scene, not an Update scan.
                foreach(Material source in Resources.FindObjectsOfTypeAll<Material>())
                    if(source!=null && source.name=="flare 1")
                    {_motorFlameMaterial=new Material(source) {name="LOSAT_Short_Motor_Flame"};break;}
                if(_motorFlameMaterial==null)
                {_motorMaterialMissing=true;WarnEffects(new InvalidOperationException("Native flare material unavailable; smoke/audio retained"));return;}
            }
            MotorFlame flame;
            if(!_motorFlames.TryGetValue(round,out flame) || flame.Particles==null)
            {
                var node=new GameObject("LOSAT_MotorFlame");node.layer=round.gameObject.layer;
                node.SetActive(false);node.transform.SetParent(round.transform,false);
                node.transform.localPosition=new Vector3(0f,0f,-.70f);
                node.transform.localRotation=Quaternion.Euler(0f,180f,0f);
                ParticleSystem p=node.AddComponent<ParticleSystem>();
                var main=p.main;main.playOnAwake=false;main.loop=false;main.prewarm=false;
                main.duration=MotorFlameRules.BurnSeconds;main.startLifetime=MotorFlameRules.ParticleLifetime;
                main.startSpeed=MotorFlameRules.ExhaustSpeed;main.startSize=.16f;main.maxParticles=MotorFlameRules.MaxParticles;
                main.simulationSpace=ParticleSystemSimulationSpace.Local;
                // Do not use Disable: Stop+Clear during pooled reuse would
                // deactivate the emitter immediately before its next Play.
                main.stopAction=ParticleSystemStopAction.None;
                var shape=p.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Cone;shape.angle=5f;shape.radius=.028f;
                var emission=p.emission;emission.enabled=true;emission.rateOverTime=MotorFlameRules.EmissionRate;emission.rateOverDistance=0f;
                var color=p.colorOverLifetime;color.enabled=true;
                var gradient=new Gradient();
                gradient.SetKeys(new[] {new GradientColorKey(new Color(1f,.85f,.45f),0f),
                    new GradientColorKey(new Color(1f,.33f,.055f),.4f),new GradientColorKey(new Color(.40f,.055f,.015f),1f)},
                    new[] {new GradientAlphaKey(.80f,0f),new GradientAlphaKey(.60f,.45f),new GradientAlphaKey(0f,1f)});
                color.color=new ParticleSystem.MinMaxGradient(gradient);
                var size=p.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0f,1f,1f,.14f));
                var renderer=p.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=_motorFlameMaterial;
                renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.velocityScale=0f;renderer.maxParticleSize=.04f;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                flame=new MotorFlame {Particles=p};_motorFlames[round]=flame;
            }
            flame.RoundId=round.ID;
            flame.Particles.gameObject.SetActive(true);
            flame.Particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            flame.Particles.Play(false);
        }
        private void StopMissileMotor(LiveRound round)
        {
            MotorFlame flame;
            if(round!=null && _motorFlames.TryGetValue(round,out flame) && flame.RoundId==round.ID && flame.Particles!=null)
                flame.Particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void ResetMissileMotors()
        {
            foreach(MotorFlame flame in _motorFlames.Values)
                if(flame.Particles!=null) UnityEngine.Object.Destroy(flame.Particles.gameObject);
            _motorFlames.Clear();
            if(_motorFlameMaterial!=null) UnityEngine.Object.Destroy(_motorFlameMaterial);
            _motorFlameMaterial=null;_motorMaterialMissing=false;
        }
    }
}
