using System;
using GHPC.Audio;
using GHPC.Weapons;
using HarmonyLib;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        [ThreadStatic] private static bool _m8ImpactAudioScope;
        private void InstallImpactAudioTrial()
        {
            foreach(string method in new[]{"ResolveImpactAudio","EvaluateFrameImpactSounds"})
                HarmonyInstance.Patch(AccessTools.Method(typeof(LiveRound),method),
                    prefix:new HarmonyMethod(typeof(LosatService),nameof(PermitM8RoundImpactAudio)));
            foreach(string method in new[]{"PlayTerrainImpactSFX","PlayRicochetSFX","PlaySmallCalImpactSFX","PlayLargeCalImpactSFX"})
                HarmonyInstance.Patch(AccessTools.Method(typeof(ImpactSFXManager),method),
                    prefix:new HarmonyMethod(typeof(LosatService),nameof(PermitM8AmmoImpactAudio)));
            // doImpactEffect must still run: it performs the visual effect.
            // Its direct simple-audio call lacks a round/ammo argument, so use
            // a bounded synchronous scope restored even on native exceptions.
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiveRound),"doImpactEffect"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(BeginM8ImpactAudioScope)),
                finalizer:new HarmonyMethod(typeof(LosatService),nameof(EndM8ImpactAudioScope)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(ImpactSFXManager),"PlaySimpleImpactAudio"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(PermitM8SimpleImpactAudio)));
        }
        private static bool IsM8ImpactRound(LiveRound round)
        {
            LosatService owner=SalvoOwner;TrackedMissile tracked;
            return owner!=null && round!=null && owner._missileLookup.TryGetValue(round,out tracked) && tracked.RoundId==round.ID;
        }
        private static bool PermitM8RoundImpactAudio(LiveRound __instance)
        {return !IsM8ImpactRound(__instance);}
        private static bool PermitM8AmmoImpactAudio(AmmoType __1)
        {
            LosatService owner=SalvoOwner;
            if(owner==null || __1==null) return true;
            foreach(TrackedMissile tracked in owner._missiles)
                if(ReferenceEquals(tracked.Ammo,__1) && IsM8ImpactRound(tracked.Round)) return false;
            return true;
        }
        private static void BeginM8ImpactAudioScope(LiveRound __instance,out bool __state)
        {__state=_m8ImpactAudioScope;_m8ImpactAudioScope=IsM8ImpactRound(__instance);}
        private static Exception EndM8ImpactAudioScope(Exception __exception,bool __state)
        {_m8ImpactAudioScope=__state;return __exception;}
        private static bool PermitM8SimpleImpactAudio()
        {return !_m8ImpactAudioScope;}
    }
}
