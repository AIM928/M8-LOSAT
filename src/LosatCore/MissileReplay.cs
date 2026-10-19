using System.Collections.Generic;
using GHPC;
using GHPC.Weapons;
using HarmonyLib;
using NativeShotInfo=GHPC.Weapons.ShotInfo;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private readonly HashSet<NativeShotInfo> _earlyReplayShots=new HashSet<NativeShotInfo>();

        private void InstallMissileReplayHooks()
        {
            HarmonyInstance.Patch(AccessTools.Method(typeof(AarController),"StartAar"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(PrepareMissileReplayPrefix)) {priority=HarmonyLib.Priority.First});
            HarmonyInstance.Patch(AccessTools.Method(typeof(AarController),"AddShot",new[] {typeof(NativeShotInfo)}),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(ReplayShotSubmissionPrefix)));
        }

        private static bool MayRefreshMissileMotor(TrackedMissile t)
        {
            return !t.NativeImpact && !t.GuidanceReleased && t.Round.Guided;
        }

        private static bool ReplayShotSubmissionPrefix(NativeShotInfo __0)
        {
            // Native eventual termination may submit a ShotInfo already flushed
            // on entry to AAR. Only deduplicate our own early submissions.
            return SalvoOwner==null || __0==null || !SalvoOwner._earlyReplayShots.Contains(__0);
        }

        private static void PrepareMissileReplayPrefix()
        {
            LosatService owner=SalvoOwner;
            if(owner==null) return;
            // Native StartAar snapshots completed SessionShots and then freezes
            // physics. A KE round that exited a tank may still be flying.
            // Flush at this boundary, never during penetration or every frame.
            AarController aar=AarController.Instance;
            if(aar==null) return;
            var shots=GetField(aar,"SessionShots") as LinkedList<NativeShotInfo>;
            if(shots==null) return;
            foreach(TrackedMissile t in owner._missiles)
            {
                LiveRound round=t.Round;
                if(!t.NativeImpact || round==null || round.Pooled || round.IsDestroyed || round.ID!=t.RoundId) continue;
                NativeShotInfo shot=round.ShotInfo;
                if(shot==null || !shot.IsHit || owner._earlyReplayShots.Contains(shot) || shots.Contains(shot)) continue;
                // Preserve original identity, target poses, armor frames and
                // child spall. No synthetic shot, forced detonation or truncation.
                shot.StopPosition=round.transform.position;
                shot.Story=round.Story;
                shot.NotifyShotTerminated();
                if(shots.Contains(shot)) owner._earlyReplayShots.Add(shot);
            }
        }
    }
}
