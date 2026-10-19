using GHPC.Vehicle;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private bool UpdateTrackedTarget(TargetLock target,UnityEngine.Camera camera,Vehicle self,bool check)
        {
            if(!LockCandidateAlive(target.Vehicle)) return false;
            if(check)
            {
                bool hardBlocked;
                bool observed=TargetBodyVisibilityFrom(camera.transform.position,target.Vehicle,self,target.LocalBounds,true,out hardBlocked);
                target.Visible=LockRecoveryRules.Update(ref target.ObscuredSince,ref target.ClearSince,observed,hardBlocked,Time.time);
                if(target.Visible) RefreshLockRange(target,camera,target.Vehicle.transform.TransformPoint(target.LocalAimPoint));
            }
            float obscured=target.ObscuredSince<0f ? 0f : Time.time-target.ObscuredSince;
            target.Reacquiring=TargetLockRules.Reacquiring(obscured);
            // If the body reappears just before the deadline, finish its short
            // clear-visibility settling period rather than drop a valid recovery.
            if(TargetLockRules.MustRelease(true,true,obscured) && target.ClearSince<0f) return false;
            // Only confirmed visible targets update the frame. Recovery rays
            // test this SAME vehicle each tick; never search for a different ID.
            // Retain smooth frame projection between low-frequency LOS checks.
            if(target.Visible) ProjectLockFrame(camera,target);
            return true;
        }
    }
}
