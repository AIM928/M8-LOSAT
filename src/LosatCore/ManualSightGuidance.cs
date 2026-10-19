using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private void GuideManualSight(TrackedMissile tracked)
        {
            // Opt-in only for an unassigned PLAYER launch. Never alter a native
            // AI shot whose target binding failed, or a locked missile's arc.
            SalvoWeapon source;
            if(!tracked.ManualSightGuidance || tracked.GuidanceReleased || tracked.SourceWeapon==null ||
                !SalvoWeapons.TryGetValue(tracked.SourceWeapon,out source) || !IsPlayerWeapon(source) ||
                source.Vehicle==null || source.Weapon.FCS==null) return;
            // The sight axis is not the body-fixed 3-degree tube, nor a barrel
            // direction with ballistic superelevation. It follows the reticle.
            Vector3 origin,direction;
            if(!GetManualSightRay(source.Weapon,out origin,out direction))return;
            if(Time.time>=tracked.NextManualAim)
            {
                tracked.NextManualAim=Time.time+.1f;
                tracked.ManualAimValid=false;
                if(!ThermalSmokeAtOrigin(origin))
                {
                    int count=Physics.RaycastNonAlloc(origin,direction,_lockHits,TargetLockRules.MaximumRange,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
                    if(count<_lockHits.Length)
                    {
                        float nearest=float.PositiveInfinity;bool surface=false;
                        for(int i=0;i<count;i++)
                        {
                            Collider collider=_lockHits[i].collider;
                            if(collider==null || M8ContactRules.NativePassThrough(collider)) continue;
                            LockBlocker kind;var owner=LockRayOwner(collider,out kind);
                            if(owner==source.Vehicle || collider.transform.IsChildOf(source.Vehicle.transform) ||
                                (owner==null && kind==LockBlocker.Ignore)) continue;
                            if(_lockHits[i].distance<nearest)
                            {nearest=_lockHits[i].distance;surface=owner!=null || kind==LockBlocker.Cover;}
                        }
                        tracked.ManualAimValid=surface && ManualSteeringRules.ReliableDistance(nearest);
                        tracked.ManualAimDistance=nearest;
                    }
                }
            }
            Vector3 goal=ManualArcDirection(tracked,origin,direction);
            // Measured surfaces already provide exact point convergence;
            // preserve that approach, especially on steep downhill shots.
            if(!tracked.ManualAimValid)
                goal=ConvergeManualLateral(goal,origin,direction,tracked.RoundTransform.position,tracked.FlightSpeed);
            if(goal.sqrMagnitude>.01f) tracked.Round.GoalVector=SmoothManualCommand(tracked,goal);
        }
        private static Vector3 ConvergeManualLateral(Vector3 goal,Vector3 origin,Vector3 axis,Vector3 position,float speed)
        {
            // Converge both physical banks onto ONE optical line early, even
            // without a reliable range. The old long 3-D lookahead retained a
            // same-bank offset on short shots. Keep vertical/arc demand intact;
            // this only shortens horizontal cross-track convergence, with the
            // existing per-round smoothing and native turn limit still applied.
            Vector3 horizontal=new Vector3(axis.x,0f,axis.z);
            if(horizontal.sqrMagnitude<.0001f)return goal;
            horizontal=horizontal.normalized;
            Vector3 side=new Vector3(horizontal.z,0f,-horizontal.x);
            float ahead=Vector3.Dot(goal,horizontal);
            if(ahead<=0f)return goal; // Never turn back after passing a surface.
            float error=Vector3.Dot(position-origin,side);
            float desired=-error*ahead/(ManualSteeringRules.Lead(speed)*.5f);
            return goal+side*(desired-Vector3.Dot(goal,side));
        }
        private static Vector3 SmoothManualCommand(TrackedMissile tracked,Vector3 goal)
        {
            Vector3 desired=goal.normalized;
            float now=Time.time;
            if(!tracked.ManualCommandValid || now<tracked.ManualCommandTime)
            {
                tracked.ManualCommandValid=true;
                tracked.ManualCommandTime=now;
                return tracked.ManualCommand=desired;
            }
            float elapsed=now-tracked.ManualCommandTime;
            // Native guidance can run repeatedly for the same rendered time.
            // Advance once per elapsed game time, not once per callback/missile.
            if(elapsed<=0f)return tracked.ManualCommand;
            tracked.ManualCommandTime=now;
            // Short exponential command shaping, not a change to native angular
            // speed, projectile position, collision or muzzle velocity. Each
            // launch owns its history, so three active rounds cannot share lag.
            float weight=(float)(1d-System.Math.Exp(-elapsed/.025d));
            Vector3 command=Vector3.Lerp(tracked.ManualCommand,desired,weight);
            if(command.sqrMagnitude>.000001f)tracked.ManualCommand=command.normalized;
            return tracked.ManualCommand;
        }
        private static Vector3 ManualArcDirection(TrackedMissile tracked,Vector3 origin,Vector3 axis)
        {
            Vector3 position=tracked.RoundTransform.position;
            Vector3 direct=ManualSightDirection(origin,axis,position,tracked.FlightSpeed,
                tracked.ManualAimValid,tracked.ManualAimDistance);
            if(tracked.ManualArcEnded) return direct;
            if(!tracked.ManualAimValid || !ManualSteeringRules.ReliableDistance(tracked.ManualAimDistance))
            {tracked.ManualArcEnded=true;return direct;}
            Vector3 endpoint=origin+axis*tracked.ManualAimDistance;
            Vector3 span=endpoint-tracked.LaunchPosition;float range=span.magnitude;
            float height=ManualSteeringRules.ArcHeight(range);
            if(height==0f) {tracked.ManualArcEnded=true;return direct;}
            float along=Vector3.Dot(position-tracked.LaunchPosition,span/range);
            if(!tracked.ManualArcStarted)
            {
                tracked.ManualArcStarted=true;
                tracked.ManualArcAxis=axis;tracked.ManualArcRange=range;
            }
            // End, rather than restart, the climb after a big reticle/range
            // change, loss of ranging, or terminal entry. No late second hump.
            if(!ManualSteeringRules.StableAim(Vector3.Dot(axis,tracked.ManualArcAxis),tracked.ManualArcRange,range) ||
                ManualSteeringRules.Terminal(along/range,range-along,tracked.FlightSpeed))
            {tracked.ManualArcEnded=true;return direct;}
            Vector3 baseline=Vector3.Lerp(tracked.LaunchPosition,endpoint,Mathf.Clamp01(along/range));
            if(position.y>baseline.y+height+.5f)
            {tracked.ManualArcEnded=true;return direct;} // Native finite-turn overshoot: stop requesting climb.
            float progress=Mathf.Clamp01((along+ManualSteeringRules.Lead(tracked.FlightSpeed))/range);
            Vector3 goal=Vector3.Lerp(tracked.LaunchPosition,endpoint,progress);
            goal.y+=ManualSteeringRules.ArcOffset(progress,range);
            return goal-position;
        }
        private static Vector3 ManualSightDirection(Vector3 origin,Vector3 axis,Vector3 position,
            float speed,bool reliable,float distance)
        {
            // Cache RANGE only, not a vehicle or world point. Reticle movement
            // updates the desired ray; the per-launch command transition above
            // smooths its response. Manual fire never becomes a target lock.
            if(reliable && ManualSteeringRules.ReliableDistance(distance))
            {
                Vector3 toPoint=origin+axis*distance-position;
                float remaining=Vector3.Dot(toPoint,axis);
                // A near-zero forward distance used to amplify tiny lateral
                // errors into near-sideways commands, then jump to the long
                // sight-line fallback just after crossing the endpoint. Keep a
                // bounded forward approach margin through that terminal region.
                float margin=ManualSteeringRules.Lead(speed)*.25f;
                // A percentage of the FULL range used to leave a 40-67.5 m
                // virtual approach margin on long shots, diluting terminal
                // correction. Keep only a small near-zero cushion. Native turn
                // limits and per-round command shaping still apply.
                margin=Mathf.Min(margin,Mathf.Min(2f,distance*.002f));
                if(remaining>0f) return toPoint+axis*Mathf.Max(0f,margin-remaining);
            }
            // No reliable surface (sky, dense hit buffer, smoke) or already past
            // the point: steer toward the sight LINE with a speed-scaled forward
            // lookahead. No fictitious range endpoint, added climb, or U-turn.
            float along=Mathf.Max(0f,Vector3.Dot(position-origin,axis));
            return origin+axis*(along+ManualSteeringRules.Lead(speed))-position;
        }
    }
}
