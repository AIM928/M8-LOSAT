using System.Collections.Generic;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private sealed class RocketAudioState
        {internal AudioSource Source;internal bool Muted;}
        private readonly Dictionary<LiveRound,List<RocketAudioState>> _rocketAudioMute=new Dictionary<LiveRound,List<RocketAudioState>>();
        private void InstallNativeRocketAudioMute()
        {
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiveRound),"RestoreFromPool"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(BeforeNativeRocketReuse)));
        }
        private void MuteNativeRocketLoop(LiveRound round)
        {
            if(round==null) return;
            RestoreNativeRocketLoop(round);
            List<RocketAudioState> states=null;
            foreach(AudioSource source in round.GetComponentsInChildren<AudioSource>(true))
            {
                if(source==null || source.clip==null || source.clip.name!="TOW rocket loop") continue;
                if(states==null){states=new List<RocketAudioState>();_rocketAudioMute.Add(round,states);}
                states.Add(new RocketAudioState {Source=source,Muted=source.mute});
                source.mute=true;source.Stop();
            }
        }
        private static void BeforeNativeRocketReuse(LiveRound __instance)
        {if(SalvoOwner!=null)SalvoOwner.RestoreNativeRocketLoop(__instance);}
        private void RestoreNativeRocketLoop(LiveRound round)
        {
            List<RocketAudioState> states;
            if(ReferenceEquals(round,null) || !_rocketAudioMute.TryGetValue(round,out states)) return;
            _rocketAudioMute.Remove(round);
            foreach(RocketAudioState state in states)
                if(state.Source!=null)state.Source.mute=state.Muted;
        }
        private void ResetNativeRocketLoops()
        {
            foreach(List<RocketAudioState> states in _rocketAudioMute.Values)
                foreach(RocketAudioState state in states)
                    if(state.Source!=null)state.Source.mute=state.Muted;
            _rocketAudioMute.Clear();
        }
    }
}
