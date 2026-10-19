namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        internal static void ConfigureDirectFlight(AmmoType ammo)
        {
            // Only called on the converted vehicle's private ammo clones.
            // Keep native steering, collision, arming and guidance damage checks.
            ammo.Flight=AmmoType.FlightPattern.Direct;
            ammo.SpiralPower=0f;
            ammo.SpiralAngularRate=0f;
            ammo.NoisePowerX=0f;
            ammo.NoisePowerY=0f;
            ammo.GuidanceLockoutTime=0f;
            ammo.LockoutForcedElevation=0f;
        }
    }
}
