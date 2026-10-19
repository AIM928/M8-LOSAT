using GHPC.Equipment;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal static class M8ContactRules
    {
        // Match LiveRound.penCheck's explicit notify-and-pass-through branch.
        // Do NOT ignore all triggers: native damage zones may also be triggers.
        internal static bool NativePassThrough(Collider collider)
        {return collider!=null&&collider.CompareTag("Detection")&&collider.GetComponent<ShotDetectionZone>()!=null;}
    }
}
