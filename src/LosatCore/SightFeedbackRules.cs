using System;
namespace M8LOSATM2.LosatCore
{
    // Presentation only: no target eligibility, guidance or firing state changes.
    internal sealed class SightFeedbackState
    {
        internal const float ToneInterval=.20f; // Existing tone is 120 ms.
        internal const float PulseLifetime=.36f;
        private int _pending;
        private float _nextTone,_pulseStart=float.NegativeInfinity;
        internal int Pending {get {return _pending;}}
        internal void BeginPulse(float now) {_pulseStart=now;}
        internal bool PulseOn(float now)
        {
            float elapsed=now-_pulseStart;
            return elapsed>=0f && elapsed<PulseLifetime && (elapsed<.14f || elapsed>=.22f);
        }
        internal void Enqueue(float now)
        {
            // Existing target cap bounds scan events; spacing survives another E press.
            if(_pending<int.MaxValue) _pending++;
        }
        internal bool TakeTone(float now)
        {
            if(_pending==0 || now<_nextTone || float.IsNaN(now)) return false;
            _pending--;_nextTone=now+ToneInterval;return true;
        }
        internal void Clear()
        {_pending=0;_nextTone=0;_pulseStart=float.NegativeInfinity;}
    }
}
