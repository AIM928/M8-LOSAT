using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private FMOD.Sound _lockTone;
        private bool _lockToneFailed;
        private readonly SightFeedbackState _sightFeedback=new SightFeedbackState();
        private OpticsUpgrade _sightFeedbackOwner;
        private void PlayLockConfirmation()
        {
            OpticsUpgrade owner=CurrentLockSight();if(owner==null) return;
            if(_sightFeedbackOwner!=null && _sightFeedbackOwner!=owner) ClearSightFeedback();
            _sightFeedbackOwner=owner;_sightFeedback.Enqueue(UnityEngine.Time.unscaledTime);
        }
        private void BeginSightScanPulse(OpticsUpgrade owner)
        {
            if(_sightFeedbackOwner!=null && _sightFeedbackOwner!=owner) ClearSightFeedback();
            _sightFeedbackOwner=owner;_sightFeedback.BeginPulse(UnityEngine.Time.unscaledTime);
        }
        private void ClearSightFeedback()
        {_sightFeedback.Clear();_sightFeedbackOwner=null;}
        private void UpdateSightFeedback()
        {
            if(_sightFeedbackOwner==null) return;
            if(!LockInputReady() || CurrentLockSight()!=_sightFeedbackOwner)
            {ClearSightFeedback();return;}
            // Update, not OnGUI: one event cannot replay on each repaint.
            if(_sightFeedback.TakeTone(UnityEngine.Time.unscaledTime)) PlayLockToneNow();
        }
        private void PlayLockToneNow()
        {
            if(_lockToneFailed) return;
            try
            {
                FMOD.System core=FMODUnity.RuntimeManager.CoreSystem;
                if(!core.hasHandle()) return;
                if(!_lockTone.hasHandle())
                {
                    byte[] wave=LockToneRules.BuildWave();
                    var info=new FMOD.CREATESOUNDEXINFO {cbsize=Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO)),length=(uint)wave.Length};
                    CheckLockAudio(core.createSound(wave,FMOD.MODE.OPENMEMORY|FMOD.MODE._2D|FMOD.MODE.CREATESAMPLE|FMOD.MODE.LOOP_OFF,ref info,out _lockTone));
                }
                FMOD.Channel channel;
                CheckLockAudio(core.playSound(_lockTone,default(FMOD.ChannelGroup),true,out channel));
                CheckLockAudio(channel.setVolume(.50f));
                CheckLockAudio(channel.setPaused(false));
            }
            catch(Exception ex)
            {
                _lockToneFailed=true;
                MelonLogger.Warning(Prefix+"lock confirmation audio unavailable; locking remains active: "+ex.Message);
            }
        }
        private static void CheckLockAudio(FMOD.RESULT result)
        {if(result!=FMOD.RESULT.OK) throw new InvalidOperationException(result.ToString());}
        private void ResetLockConfirmation()
        {
            ClearSightFeedback();
            if(_lockTone.hasHandle()) _lockTone.release();
            _lockTone=default(FMOD.Sound);_lockToneFailed=false;
        }
        public void OnDeinitializeMelon() {UnityEngine.Camera.onPostRender-=CaptureSightFrames;ResetLockConfirmation();ResetLOSATOptics();ResetLaunchAudio();ResetSonicBoomAudio();ResetLaunchEffects();}
    }
}
