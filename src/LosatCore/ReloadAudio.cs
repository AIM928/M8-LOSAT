using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private static readonly System.Reflection.MethodInfo ReloadSoundStart=AccessTools.Method(typeof(AmmoFeed),"StartAutoloaderAudio");
        private static readonly System.Reflection.MethodInfo ReloadSoundStop=AccessTools.Method(typeof(AmmoFeed),"StopAutoloaderAudio");
        private static readonly System.Reflection.MethodInfo ReloadStageSound=AccessTools.Method(typeof(AmmoFeed),"HandleStageAudio");
        private void InstallReloadAudioHook()
        {
            // Native impact sounds restored; only the inherited rocket loop stays muted.
            InstallNativeRocketAudioMute();
            HarmonyInstance.Patch(AccessTools.Method(typeof(AmmoFeed),"FeedNewClip"),
                postfix:new HarmonyMethod(typeof(LosatService),nameof(ReloadSoundsBeginPostfix)));
            // Only the registered M8 feeds are silent. Keep native timing,
            // ammunition transitions and stop/cleanup methods untouched.
            HarmonyInstance.Patch(AccessTools.Method(typeof(AmmoFeed),"StartAutoloaderAudio"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(M8ReloadAudioPrefix)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(AmmoFeed),"HandleStageAudio"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(M8ReloadAudioPrefix)));
            // WeaponAudio has an independent one-shot reload path, separate
            // from AmmoFeed's autoloader/stage sounds. Do not silence other guns.
            HarmonyInstance.Patch(AccessTools.Method(typeof(WeaponAudio),"Reload"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(M8WeaponReloadAudioPrefix)));
        }
        private static bool M8WeaponReloadAudioPrefix(WeaponAudio __instance)
        {
            if(__instance==null) return true;
            foreach(SalvoWeapon state in SalvoWeapons.Values)
                if(state.Weapon!=null && state.Weapon.WeaponSound==__instance) return false;
            return true;
        }
        private static bool M8ReloadAudioPrefix(AmmoFeed __instance)
        {return __instance==null || !SalvoFeeds.ContainsKey(__instance);}
        private static void ReloadSoundsBeginPostfix(AmmoFeed __instance)
        {
            SalvoWeapon state;
            if(__instance!=null && __instance.Reloading && SalvoFeeds.TryGetValue(__instance,out state)) state.ReloadSounds.Begin();
        }
        private static void UpdateReloadAudio(SalvoWeapon state)
        {
            if(state.ReloadAudioFailed) return;
            try { UpdateReloadAudioCore(state); }
            catch(System.Exception error)
            {
                state.ReloadAudioFailed=true;
                MelonLoader.MelonLogger.Warning(Prefix+"Reload audio repeats disabled for this launcher: "+error.GetBaseException().Message);
            }
        }
        private static void UpdateReloadAudioCore(SalvoWeapon state)
        {
            // Fixed, preloaded M8 tubes have no audible reload mechanism.
            // Do not replay the inherited TOW/autoloader or stage sounds.
        }
    }
}
