using GHPC.Weapons;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private void RegisterGuidanceRound(SalvoWeapon state,LiveRound round)
        {
            TrackedMissile t;
            if(round==null || !_missileLookup.TryGetValue(round,out t) || t.RoundId!=round.ID) return;
            t.SourceWeapon=state.Weapon;
            if(!state.Guidance.Register(t.RoundId)) {round.Guided=false;t.GuidanceReleased=true;}
        }
        private void FinishGuidance(TrackedMissile t,bool terminate)
        {
            if(t.GuidanceReleased) return;
            // Stop native control BEFORE freeing the slot, not just a HUD lock.
            if(terminate && t.Round!=null && t.Round.ID==t.RoundId && !t.Round.Pooled) t.Round.Guided=false;
            t.GuidanceReleased=true;
            SalvoWeapon state;
            if(t.SourceWeapon!=null && SalvoWeapons.TryGetValue(t.SourceWeapon,out state)) state.Guidance.Release(t.RoundId);
            EndAttackWithoutHit(t);
        }
        private void UpdateGuidanceChannel(TrackedMissile t)
        {
            if(t.GuidanceReleased) return;
            if(t.PassedTarget || (t.TargetGuidance!=null && t.TargetGuidance.Damaged)) {FinishGuidance(t,true);return;}
            if(!t.Round.Guided) {FinishGuidance(t,false);return;}
            if(t.TargetAssignment!=null && !LockCandidateAlive(t.TargetVehicle)) {FinishGuidance(t,true);return;}
            if(Time.unscaledTime<t.NextGuidanceVisibility) return;
            t.NextGuidanceVisibility=Time.unscaledTime+TargetLockRules.VisibilityInterval;
            SalvoWeapon state;
            if(t.SourceWeapon==null || !SalvoWeapons.TryGetValue(t.SourceWeapon,out state)) {FinishGuidance(t,true);return;}
            Transform optic=NativeSightAim(state.Weapon);
            if(optic==null) {FinishGuidance(t,true);return;}
            bool hardBlocked;
            if(t.TargetAssignment!=null)
                TargetBodyVisibilityFrom(optic.position,t.TargetVehicle,state.Vehicle,CenterBodyBounds(t.TargetVehicle),true,out hardBlocked);
            else
            {
                Vector3 delta=t.RoundTransform.position-optic.position;bool clear;
                RayVehicle(optic.position,delta.normalized,delta.magnitude,state.Vehicle,out clear,out hardBlocked);
            }
            t.GuidanceObscured=hardBlocked;
            if(!hardBlocked) t.HardOccludedSince=-1f;
            else if(t.HardOccludedSince<0f) t.HardOccludedSince=Time.time;
            else if(TargetLockRules.MustRelease(true,true,Time.time-t.HardOccludedSince)) FinishGuidance(t,true);
        }
    }
}
