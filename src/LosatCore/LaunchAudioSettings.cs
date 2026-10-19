using System;
using MelonLoader;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private MelonPreferences_Entry<float> _launchVolume;

        private void InitializeLaunchAudioSettings()
        {
            // Use the loader's existing preferences file. Only this category is
            // saved; never replace the user's file with a bundled configuration.
            try
            {
                var category = MelonPreferences.CreateCategory("M8_LOSAT", "M8 LOSAT");
                _launchVolume = category.CreateEntry<float>("LaunchVolume", LaunchVolumeRules.DefaultVolume,
                    "Missile launch volume",
                    "0 = mute, 0.65 = default, 1 = original v0.7.57 gain with softened tone. Values are clamped to 0..1.");
                category.SaveToFile(false);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Launch volume preferences unavailable; quieter default retained: " + e.Message);
            }
        }

        private float GetLaunchAudioVolume()
        {
            // Read only when a missile fires, not on every frame. Scene resets
            // do not reset the preference or change other audio channels.
            return LaunchVolumeRules.Gain(_launchVolume == null ?
                LaunchVolumeRules.DefaultVolume : _launchVolume.Value);
        }
    }
}
