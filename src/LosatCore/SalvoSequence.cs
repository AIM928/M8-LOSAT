using System;

namespace M8LOSATM2.LosatCore
{
    // A real button release is required; reloads and pauses never re-arm a held trigger.
    internal sealed class SalvoSequence
    {
        internal const int TubeCount = 12;
        internal const int ReserveCapacity = 0;
        internal const float Interval = .30f;
        internal bool Latched;

        internal void ObserveButton(bool held)
        {
            if (!held) Latched = false;
        }

        internal bool AllowPress(bool buttonDown)
        {
            return buttonDown && !Latched;
        }

        internal void SuccessfulShot()
        {
            Latched = true;
        }
    }

    // Simulation time, not wall-clock time: pause/time compression follows GHPC.
    // Admission never consumes the cooldown. Only a successful native Fired event does.
    internal sealed class AiShotLimiter
    {
        internal const float MinimumInterval = 4f;
        internal const int MaximumActiveAiMissiles = 4;
        internal float NextAllowedTime = float.NegativeInfinity;

        internal bool AllowAttempt(float now, bool ownMissileActive, int activeAiMissiles)
        {
            return now >= NextAllowedTime && !ownMissileActive &&
                activeAiMissiles < MaximumActiveAiMissiles;
        }

        internal void SuccessfulShot(float now)
        {
            NextAllowedTime = now + MinimumInterval;
        }
    }
}
