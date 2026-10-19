using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using MelonLoader;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private FMOD.Sound _sonicSound;
        private bool _sonicFailed;
        private readonly FMOD.Channel[] _sonicChannels=new FMOD.Channel[8];
        private readonly TrackedMissile[] _sonicOwners=new TrackedMissile[8];
        private int _sonicSlot;
        private bool EnsureSonicBoomSound()
        {
            if(_sonicSound.hasHandle()) return true;
            if(_sonicFailed) return false;
            try{
                FMOD.System core=FMODUnity.RuntimeManager.CoreSystem;if(!core.hasHandle()) return false;
                byte[] wave;
                using(Stream stream=typeof(LosatService).Assembly.GetManifestResourceStream("M8LOSATM2.LosatCore.Missile.SonicBoom"))
                using(var memory=new MemoryStream()){if(stream==null) throw new FileNotFoundException("LOSAT sonic boom");stream.CopyTo(memory);wave=memory.ToArray();}
                var info=new FMOD.CREATESOUNDEXINFO{cbsize=Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),length=(uint)wave.Length};
                CheckLockAudio(core.createSound(wave,FMOD.MODE.OPENMEMORY|FMOD.MODE._3D|FMOD.MODE._3D_INVERSEROLLOFF|FMOD.MODE.CREATESAMPLE|FMOD.MODE.LOOP_OFF,ref info,out _sonicSound));
                return true;
            }catch(Exception e){FailSonicBoom(e);return false;}
        }
        private static bool SonicRoundAlive(TrackedMissile tracked)
        {
            return tracked!=null && tracked.Round!=null && !tracked.Round.Pooled && !tracked.Round.IsDestroyed &&
                tracked.Round.ID==tracked.RoundId && tracked.RoundTransform!=null && !tracked.NativeImpact;
        }
        private void UpdateSonicBoomAudio()
        {
            if(_sonicFailed) return;
            bool paused=Time.timeScale<=0f || GHPC.AarController.InAar;
            try{
                for(int i=0;i<_sonicChannels.Length;i++){
                    if(!_sonicChannels[i].hasHandle()) continue;
                    bool playing;FMOD.RESULT result=_sonicChannels[i].isPlaying(out playing);
                    if(result!=FMOD.RESULT.OK || !playing || !SonicRoundAlive(_sonicOwners[i])){StopSonicSlot(i);continue;}
                    CheckLockAudio(_sonicChannels[i].setPaused(paused));
                    PositionSonicChannel(_sonicChannels[i],_sonicOwners[i].RoundTransform.position);
                }
                if(paused) return;
                for(int i=0;i<_missiles.Count;i++){
                    TrackedMissile tracked=_missiles[i];
                    if(!SonicRoundAlive(tracked)) continue;
                    if(!SonicBoomRules.TryStart(ref tracked.SonicBoom,Time.time,tracked.Round.CurrentSpeed,true,false)) continue;
                    if(!EnsureSonicBoomSound()) continue;
                    int slot=_sonicSlot++%_sonicChannels.Length;StopSonicSlot(slot);
                    FMOD.Channel channel;
                    CheckLockAudio(FMODUnity.RuntimeManager.CoreSystem.playSound(_sonicSound,default(FMOD.ChannelGroup),true,out channel));
                    _sonicChannels[slot]=channel;_sonicOwners[slot]=tracked;
                    PositionSonicChannel(channel,tracked.RoundTransform.position);
                    CheckLockAudio(channel.set3DMinMaxDistance(20f,2500f));
                    CheckLockAudio(channel.setVolume(GetLaunchAudioVolume()*SonicBoomRules.GainScale));
                    CheckLockAudio(channel.setPaused(false));
                }
            }catch(Exception e){FailSonicBoom(e);}
        }
        private static void PositionSonicChannel(FMOD.Channel channel,Vector3 position)
        {
            FMOD.VECTOR p=new FMOD.VECTOR{x=position.x,y=position.y,z=position.z},v=default(FMOD.VECTOR);
            // Follow position; zero Doppler velocity keeps the brief crack from
            // pitch-flipping at extreme missile speed. No camera/source teleport.
            CheckLockAudio(channel.set3DAttributes(ref p,ref v));
        }
        private void StopSonicSlot(int slot)
        {
            if(_sonicChannels[slot].hasHandle()) _sonicChannels[slot].stop();
            _sonicChannels[slot]=default(FMOD.Channel);_sonicOwners[slot]=null;
        }
        private void FailSonicBoom(Exception e)
        {
            if(!_sonicFailed) MelonLogger.Warning(Prefix+"sonic boom audio disabled safely: "+e.Message);
            ResetSonicBoomAudio();_sonicFailed=true;
        }
        private void ResetSonicBoomAudio()
        {
            for(int i=0;i<_sonicChannels.Length;i++) StopSonicSlot(i);
            if(_sonicSound.hasHandle()) _sonicSound.release();
            _sonicSound=default(FMOD.Sound);_sonicSlot=0;_sonicFailed=false;
        }
    }
}
