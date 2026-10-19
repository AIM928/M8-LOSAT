using System;

namespace M8LOSATM2.LosatCore
{
    internal static class LaunchVolumeRules
    {
        internal const float DefaultVolume = 0.65f;
        private const float OriginalGain = 2.6f;

        internal static float Gain(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume)) volume = DefaultVolume;
            if (volume < 0f) volume = 0f;
            if (volume > 1f) volume = 1f;
            return OriginalGain * volume;
        }
    }
}
