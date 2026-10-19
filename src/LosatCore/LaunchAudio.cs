using System;
using System.IO;
using System.Runtime.InteropServices;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private FMOD.Sound _launchSound;
        private bool _launchAudioFailed;
        private readonly FMOD.Channel[] _launchChannels=new FMOD.Channel[8];
        private readonly WeaponSystem[] _launchOwners=new WeaponSystem[8];
        private readonly bool[] _launchFading=new bool[8];
        private int _launchChannelIndex;
        private void InstallLaunchAudioHook()
        {
            HarmonyInstance.Patch(AccessTools.Method(typeof(WeaponAudio),"FinalStartLoop"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(NativeLaunchAudioPrefix)));
        }
        private static bool NativeLaunchAudioPrefix(WeaponAudio __instance)
        {
            if(SalvoOwner==null) return true;
            foreach(SalvoWeapon s in SalvoWeapons.Values)
                if(s.Weapon!=null && s.Weapon.WeaponSound==__instance)
                    return !SalvoOwner.EnsureLaunchSound();
            return true;
        }
        private bool EnsureLaunchSound()
        {
            if(_launchSound.hasHandle()) return true;
            if(_launchAudioFailed) return false;
            try
            {
                FMOD.System core=FMODUnity.RuntimeManager.CoreSystem;
                if(!core.hasHandle()) return false;
                byte[] wave;
                using(Stream s=typeof(LosatService).Assembly.GetManifestResourceStream("M8LOSATM2.LosatCore.Launch.Wave"))
                using(var m=new MemoryStream()) {if(s==null) throw new FileNotFoundException("Embedded launch sound");s.CopyTo(m);wave=m.ToArray();}
                var info=new FMOD.CREATESOUNDEXINFO {cbsize=Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),length=(uint)wave.Length};
                CheckLockAudio(core.createSound(wave,FMOD.MODE.OPENMEMORY|FMOD.MODE._3D|FMOD.MODE._3D_INVERSEROLLOFF|
                    FMOD.MODE.CREATESAMPLE|FMOD.MODE.LOOP_OFF,ref info,out _launchSound));
                return true;
            }
            catch(Exception e) {_launchAudioFailed=true;MelonLogger.Warning(Prefix+"custom launch audio unavailable; native fallback: "+e.Message);return false;}
        }
        private void PlayLaunchAudio(WeaponSystem owner,Vector3 position)
        {
            if(owner==null || !EnsureLaunchSound()) return;
            try
            {
                // A new shot from this launcher replaces its long tail with a
                // short DSP-clock fade, rather than accumulating up to 8 tails.
                // Other launchers keep their own independent sound.
                for(int n=0;n<_launchChannels.Length;n++)
                    if(ReferenceEquals(_launchOwners[n],owner) && !_launchFading[n])FadeLaunchTail(n);
                int i=_launchChannelIndex++%_launchChannels.Length;
                StopLaunchChannel(i);
                FMOD.Channel channel;
                CheckLockAudio(FMODUnity.RuntimeManager.CoreSystem.playSound(_launchSound,default(FMOD.ChannelGroup),true,out channel));
                for(int n=0;n<_launchChannels.Length;n++)
                    if(n!=i && _launchChannels[n].handle==channel.handle)
                    {_launchChannels[n]=default(FMOD.Channel);_launchOwners[n]=null;_launchFading[n]=false;}
                _launchChannels[i]=channel;
                _launchOwners[i]=owner;_launchFading[i]=false;
                FMOD.VECTOR p=new FMOD.VECTOR {x=position.x,y=position.y,z=position.z},v=default(FMOD.VECTOR);
                CheckLockAudio(channel.set3DAttributes(ref p,ref v));
                CheckLockAudio(channel.set3DMinMaxDistance(40f,4000f));
                CheckLockAudio(channel.setVolume(GetLaunchAudioVolume()));
                CheckLockAudio(channel.setPaused(false));
            }
            catch(Exception e) {_launchAudioFailed=true;ResetLaunchChannels();if(_launchSound.hasHandle()) _launchSound.release();_launchSound=default(FMOD.Sound);MelonLogger.Warning(Prefix+"launch audio stopped safely: "+e.Message);}
        }
        private bool OwnedLaunchChannel(int i,out FMOD.Channel channel)
        {
            channel=_launchChannels[i];FMOD.Sound sound;bool playing;
            // A finished FMOD handle may be invalid or reused by another game
            // sound. Never fade/stop an unrelated sound through an old slot.
            return channel.hasHandle() && _launchSound.hasHandle() &&
                channel.getCurrentSound(out sound)==FMOD.RESULT.OK && sound.handle==_launchSound.handle &&
                channel.isPlaying(out playing)==FMOD.RESULT.OK && playing;
        }
        private void FadeLaunchTail(int i)
        {
            FMOD.Channel channel;
            if(!OwnedLaunchChannel(i,out channel)){StopLaunchChannel(i);return;}
            ulong clock,parent;int rate,channels;FMOD.SPEAKERMODE mode;
            // FMOD schedules this 35ms fade and stop even during a frame hitch.
            // Do not reschedule an already fading tail on the next rapid shot.
            if(channel.getDSPClock(out clock,out parent)==FMOD.RESULT.OK &&
                FMODUnity.RuntimeManager.CoreSystem.getSoftwareFormat(out rate,out mode,out channels)==FMOD.RESULT.OK && rate>0)
            {
                ulong end=parent+(ulong)Math.Max(1,rate*35/1000);
                // Fade-point gain multiplies setVolume: start at unity, not
                // the configured gain again (which would cause a volume step).
                if(channel.addFadePoint(parent,1f)==FMOD.RESULT.OK &&
                    channel.addFadePoint(end,0f)==FMOD.RESULT.OK &&
                    channel.setDelay(0,end,true)==FMOD.RESULT.OK){_launchFading[i]=true;return;}
            }
            StopLaunchChannel(i); // Failed fade only stops this owned custom voice.
        }
        private void StopLaunchChannel(int i)
        {
            FMOD.Channel channel;if(OwnedLaunchChannel(i,out channel))channel.stop();
            _launchChannels[i]=default(FMOD.Channel);_launchOwners[i]=null;_launchFading[i]=false;
        }
        private void ResetLaunchChannels()
        {for(int i=0;i<_launchChannels.Length;i++)StopLaunchChannel(i);}
        private void ResetLaunchAudio()
        {
            ResetNativeRocketLoops();
            ResetLaunchChannels();if(_launchSound.hasHandle()) _launchSound.release();
            _launchSound=default(FMOD.Sound);_launchAudioFailed=false;_launchChannelIndex=0;
        }
    }
}
