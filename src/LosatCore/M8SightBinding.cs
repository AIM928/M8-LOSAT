using GHPC.Equipment.Optics;
using GHPC.Weapons;
using System.Collections.Generic;
using UnityEngine;
using GHPC.Camera;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private static UsableOptic SelectM8DayOptic(FireControlSystem fcs)
        {
            // M2's TOW weapon may leave MainOptic pointing at Back Up Sight.
            // Bind the verified GPS/FLIR pair rather than converting that backup.
            foreach(var optic in FindM8OwnedOptics(fcs))
                if(optic!=null&&optic.FCS==fcs&&optic.slot!=null&&optic.slot.VisionType==NightVisionType.None&&optic.name=="GPS Optic")return optic;
            return fcs.MainOptic;
        }
        private static List<UsableOptic> FindM8OwnedOptics(FireControlSystem fcs)
        {
            var found=new List<UsableOptic>();
            if(fcs==null)return found;
            // CameraSlot.Awake reparents GPS/FLIR to the turret. A hierarchy
            // search under FCS alone misses the live optic and its old reticle.
            // Scene instances only, including inactive GPS/FLIR. The previous
            // all-resources scan included prefab/native asset wrappers and the
            // crash report enters GameObject.get_scene from this loop. Do not
            // dereference scene on every global resource to filter it afterward.
            var all=UnityEngine.Object.FindObjectsOfType<UsableOptic>(true);
            foreach(var optic in all)
                if(optic!=null&&optic.FCS==fcs&&!found.Contains(optic))found.Add(optic);
            foreach(var optic in fcs.GetComponentsInChildren<UsableOptic>(true))
                if(optic!=null&&(optic.FCS==fcs||optic==fcs.MainOptic)&&!found.Contains(optic))found.Add(optic);
            // Native backup optic serializes FCS=null but shares THIS vehicle's
            // FLIR object. Exact linked-slot identity is ownership evidence;
            // never select a different vehicle merely by an optic name.
            foreach(var optic in all)
            {
                if(optic==null||optic.FCS!=null||optic.slot==null||found.Contains(optic))continue;
                bool owned=false;
                foreach(var known in found)if(known.slot!=null&&known.slot==optic.slot.LinkedNightSight){owned=true;break;}
                if(owned)found.Add(optic);
            }
            return found;
        }
        private static bool IsM8SightSlot(CameraSlot slot,CameraSlot day,CameraSlot night,FireControlSystem fcs)
        {
            if(slot==null)return false;
            if(slot==day||slot==night)return true;
            return night!=null&&slot.VisionType==NightVisionType.None&&slot.LinkedNightSight==night&&
                slot.PairedOptic!=null&&(slot.PairedOptic.FCS==null||slot.PairedOptic.FCS==fcs);
        }
    }
}
