using System.Collections.Generic;
using GHPC.Effects;
using GHPC.Vehicle;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private enum LockBlocker {Ignore,Cover,ThermalSmoke}
        private readonly Dictionary<Collider,LockBlocker> _lockBlockers=new Dictionary<Collider,LockBlocker>(512);
        private bool _lockVisibilityBatch;
        private readonly Collider[] _smokeOverlaps=new Collider[32];
        private float _nextSmokeOrigin;
        private Vector3 _smokeOrigin;
        private bool _inThermalSmoke;
        private Vehicle LockRayOwner(Collider collider,out LockBlocker kind)
        {
            kind=ClassifyLockBlocker(collider);
            // A vision collider parented beneath the burning target must not
            // be mistaken for hitting the visible vehicle's actual armor.
            // Ignore cosmetic smoke even when its volume is beneath a vehicle:
            // it is neither an obstruction nor a real visible armor surface.
            if(kind==LockBlocker.ThermalSmoke ||
                (kind==LockBlocker.Ignore && (collider.GetComponentInParent<BlockerInformation>()!=null || collider.CompareTag("Smoke")))) return null;
            return ResolveLockCollider(collider);
        }
        private LockBlocker ClassifyLockBlocker(Collider collider)
        {
            LockBlocker kind;
            if(_lockVisibilityBatch && _lockBlockers.TryGetValue(collider,out kind)) return kind;
            BlockerInformation info=collider.GetComponentInParent<BlockerInformation>();
            if(info!=null || collider.CompareTag("Smoke"))
            {
                // Native IR metadata also covers volumes not created by our
                // grenade hook; explicitly identified cookoff stays cosmetic.
                bool deployed=collider.GetComponentInParent<LockSmokeSource>()!=null;
                bool cosmetic=collider.GetComponentInParent<TankEffects>()!=null ||
                    collider.GetComponentInParent<ParticleSystemVisionBlocker>()!=null ||
                    collider.GetComponentInParent<ParticleSystem>()!=null;
                // Particle vision volumes can carry IR metadata even for impact
                // dust. A real grenade's source marker wins; unmarked explosion
                // particles must not be mistaken for a deployed thermal screen.
                if(info!=null && info.IsMultispectral && (deployed || !cosmetic))
                    kind=LockBlocker.ThermalSmoke;
                else kind=LockBlocker.Ignore;
            }
            else if(collider.isTrigger || collider.CompareTag("Forest") ||
                collider.GetComponentInParent<ParticleSystemVisionBlocker>()!=null ||
                collider.GetComponentInParent<TankEffects>()!=null ||
                collider.GetComponentInParent<ParticleSystem>()!=null ||
                (collider.attachedRigidbody!=null && !collider.attachedRigidbody.isKinematic))
                kind=LockBlocker.Ignore;
            else kind=LockBlocker.Cover;
            // Reuse within one body's probe batch only. Every next query clears
            // the cache, so pooled/reparented effects never retain old cover/IR
            // state. Cap entries to avoid a large smoke cloud resizing on a frame.
            if(_lockVisibilityBatch && _lockBlockers.Count<512) _lockBlockers[collider]=kind;
            return kind;
        }
        private bool ThermalSmokeAtOrigin(Vector3 origin)
        {
            if(Time.unscaledTime<_nextSmokeOrigin && (origin-_smokeOrigin).sqrMagnitude<1f) return _inThermalSmoke;
            _nextSmokeOrigin=Time.unscaledTime+.1f;_smokeOrigin=origin;_inThermalSmoke=false;
            int n=Physics.OverlapSphereNonAlloc(origin,.02f,_smokeOverlaps,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
            for(int i=0;i<n;i++) if(_smokeOverlaps[i]!=null)
            {
                LockBlocker kind=ClassifyLockBlocker(_smokeOverlaps[i]);
                if(kind==LockBlocker.ThermalSmoke) {_inThermalSmoke=true;break;}
            }
            return _inThermalSmoke;
        }
    }
}
