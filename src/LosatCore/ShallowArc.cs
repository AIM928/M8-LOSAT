using GHPC.Weapons;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private static void LockedGuidancePostfix(MissileGuidanceUnit __instance)
        {
            LosatService owner=SalvoOwner;
            if(owner==null || __instance.Damaged) return;
            foreach(TrackedMissile tracked in owner._missiles)
            {
                LiveRound round=tracked.Round;
                if(tracked.TargetGuidance!=__instance || round==null ||
                    round.ID!=tracked.RoundId || round.Pooled || round.IsDestroyed ||
                    !round.Guided || round.IsInGuidanceLockout) continue;
                if(tracked.TargetAssignment==null)
                {owner.GuideManualSight(tracked);continue;}
                if(!tracked.GuidanceObscured && LockCandidateAlive(tracked.TargetVehicle) && tracked.TargetVehicle.GetInstanceID()==tracked.TargetAssignment.VehicleId)
                    tracked.LastTargetPosition=tracked.TargetVehicle.transform.TransformPoint(tracked.TargetLocalPoint);
                Vector3 direction=ShallowArcDirection(tracked);
                if(direction.sqrMagnitude>.01f) round.GoalVector=direction.normalized;
            }
        }
        private void PrepareShallowArc(LiveRound round,MissileGuidanceUnit guidance)
        {
            foreach(TrackedMissile t in _missiles)
            {
                if(t.Round!=round || t.RoundId!=round.ID) continue;
                t.TargetGuidance=guidance;t.LaunchPosition=round.transform.position;
                SalvoWeapon source;
                t.ManualSightGuidance=t.TargetAssignment==null && t.SourceWeapon!=null &&
                    SalvoWeapons.TryGetValue(t.SourceWeapon,out source) && IsPlayerWeapon(source);
                // Seed steering during the successful native Fired callback, not
                // one frame later from the old TOW guide transform.
                Vector3 origin,axis;
                if(t.ManualSightGuidance&&!t.GuidanceReleased&&GetManualSightRay(t.SourceWeapon,out origin,out axis))
                    round.GoalVector=ManualSightDirection(origin,axis,t.RoundTransform.position,t.FlightSpeed,false,0f).normalized;
                break;
            }
        }
        private static Vector3 ShallowArcDirection(TrackedMissile t)
        {
            // This is a steering goal, never a transform teleport. Native swept
            // collisions and angular turn limit still govern the actual trajectory.
            // Unassigned shots are never called here: native SACLOS owns them.
            Vector3 direct=t.LastTargetPosition-t.RoundTransform.position;
            if(t.PassedTarget) return t.RoundTransform.forward;
            Vector3 span=t.LastTargetPosition-t.LaunchPosition;
            Vector3 horizontal=new Vector3(span.x,0f,span.z);
            float range=horizontal.magnitude;
            if(range<100f) return t.LastTargetPosition-t.RoundTransform.position;
            Vector3 from=t.RoundTransform.position-t.LaunchPosition;
            float along=Vector3.Dot(new Vector3(from.x,0f,from.z),horizontal/range);
            if(along>=range*.9f && Vector3.Dot(direct,t.RoundTransform.forward)<0f)
            {t.PassedTarget=true;return t.RoundTransform.forward;} // No U-turn after a miss.
            if(!t.UseShallowArc) return direct; // AI keeps a direct center-body approach.
            // Short bounded lookahead follows a genuine shallow parabola and
            // tapers to the exact assigned target in the terminal phase.
            float progress=Mathf.Clamp01((along+Mathf.Clamp(t.FlightSpeed*.08f,15f,90f))/range);
            Vector3 goal=Vector3.Lerp(t.LaunchPosition,t.LastTargetPosition,progress);
            goal.y+=ShallowArcRules.Offset(progress,range);
            return goal-t.RoundTransform.position;
        }
    }
}
