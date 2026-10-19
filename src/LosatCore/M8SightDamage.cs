using GHPC.Camera;
using GHPC.Player;
using UnityEngine;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private static bool NativeSightDamaged(CameraSlot slot)
        {return slot!=null && (slot.Damaged || (slot.HitZone!=null && slot.HitZone.IsDestroyed));}
        private static bool M8SightDamaged(OpticsUpgrade sight)
        {
            if(sight==null)return false;
            // The primary tracker cannot be bypassed by switching to the M2
            // backup/head view after native damage automatically changes slots.
            // FLIR-only damage does not disable an intact daytime tracker.
            return NativeSightDamaged(sight.DaySlot) ||
                (CameraSlot.ActiveInstance==sight.ThermalSlot && NativeSightDamaged(sight.ThermalSlot));
        }
    }
}
