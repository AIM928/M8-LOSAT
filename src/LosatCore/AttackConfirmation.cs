using GHPC;
using GHPC.Vehicle;
using GHPC.Weapons;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private static void AssignedTargetImpactPostfix(LiveRound __instance,Collider __0)
        {
            LosatService owner=SalvoOwner;TrackedMissile t;
            if(owner==null || __instance==null || __0==null ||
                !owner._missileLookup.TryGetValue(__instance,out t) || t.RoundId!=__instance.ID) return;
            if(M8ContactRules.NativePassThrough(__0))return;
            // penCheck receives the collider actually swept by the native projectile.
            // Armor Unit matching also covers detached armor panels and hit zones.
            IArmor armor=__0.GetComponent<IArmor>();
            Vehicle struck=__0.GetComponentInParent<Vehicle>();
            SalvoWeapon state;
            if(t.SourceWeapon!=null && SalvoWeapons.TryGetValue(t.SourceWeapon,out state) &&
                ((armor!=null && ReferenceEquals(armor.Unit,state.Vehicle.BaseUnit)) || struck==state.Vehicle)) return;
            // Remember real swept impacts even after guidance loss. Cosmetic
            // clouds never call this path. Do not destroy/submit inside penCheck:
            // native armor, internals and spall still have work left in this step.
            bool realImpact=struck!=null || (armor!=null && armor.Unit!=null) || __0 is TerrainCollider;
            if(realImpact) t.NativeImpact=true;
            if(t.GuidanceReleased) return;
            bool assignedHit=t.TargetAssignment!=null && t.TargetVehicle!=null &&
                t.TargetVehicle.GetInstanceID()==t.TargetAssignment.VehicleId &&
                ((armor!=null && ReferenceEquals(armor.Unit,t.TargetVehicle.BaseUnit)) || struck==t.TargetVehicle);
            if(t.AttackSelection!=null && t.AttackSelection.Attack.ConfirmHit(t.RoundId,assignedHit))
            {
                owner._targetLocks.Remove(t.AttackSelection);
                if(owner._selectedLock==t.AttackSelection)
                    owner._selectedLock=owner._targetLocks.Count==0 ? null : owner._targetLocks[owner._targetLocks.Count-1];
                t.AttackSelection=null;
            }
            // A swept vehicle/ground hit terminates one channel. A miss keeps
            // its target selected; generic muzzle/explosion VFX never count.
            if(realImpact)
                owner.FinishGuidance(t,true);
        }
        private static void EndAttackWithoutHit(TrackedMissile t)
        {
            if(t.AttackSelection==null) return;
            t.AttackSelection.Attack.EndWithoutHit(t.RoundId);
            t.AttackSelection=null; // Old pooled rounds can never resolve a later retry.
        }
    }
}
