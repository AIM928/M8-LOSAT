using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using UnityEngine;

namespace M8LOSATM2
{
    internal static class M8ArmorDiscovery
    {
        internal static UniformArmor[] FindOwned(Vehicle vehicle)
        {
            var owned = new List<UniformArmor>();
            if (vehicle == null || vehicle.BaseUnit == null) return owned.ToArray();
            // Native raycast rigs leave the visual vehicle hierarchy at runtime.
            // Ownership, not transform ancestry, connects detached armor to its vehicle.
            foreach (UniformArmor armor in Resources.FindObjectsOfTypeAll<UniformArmor>())
            {
                if (armor == null || armor.gameObject == null ||
                    !armor.gameObject.scene.IsValid() ||
                    armor.gameObject.layer != GHPC.Utility.CodeUtils.LAYER_INDEX_RAYCAST_ONLY ||
                    !object.ReferenceEquals(armor.Unit, vehicle.BaseUnit)) continue;
                owned.Add(armor);
            }
            return owned.ToArray();
        }
    }
}
