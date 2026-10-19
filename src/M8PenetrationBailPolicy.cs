using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Crew;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;

namespace M8LOSATM2
{
    // Suppress only the converted M8's penetration-driven evacuation decision.
    // Native injury, incapacitation, fire, neutralization and commanded exits
    // remain untouched. In particular, do not set the global/no-bail flag.
    internal static class M8PenetrationBailPolicy
    {
        private sealed class Entry{internal Vehicle Owner;internal bool Failed;}
        private static readonly Dictionary<CrewManager,Entry> Owners=new Dictionary<CrewManager,Entry>();
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly MethodInfo Target=typeof(CrewManager).GetMethod("NotifyCrewCompartmentPenetrated",Flags,null,new[]{typeof(AmmoType)},null);
        private static readonly MethodInfo Prefix=typeof(M8PenetrationBailPolicy).GetMethod("Filter",BindingFlags.Static|BindingFlags.NonPublic);
        private static readonly MethodInfo Panic=typeof(CrewManager).GetMethod("SetCrewPanicked",Flags,null,new[]{typeof(float)},null);
        private static HarmonyLib.Harmony _hook;
        private static Action<CrewManager,float> _setPanicked;

        internal static void Register(Vehicle vehicle)
        {
            if(vehicle==null||vehicle.BaseUnit==null||vehicle.BaseUnit.CrewManager==null||Target==null||Prefix==null||Panic==null)
                throw new InvalidOperationException("Native M8 penetration evacuation contract unavailable.");
            var crew=vehicle.BaseUnit.CrewManager;
            if(Owners.ContainsKey(crew))throw new InvalidOperationException("M8 penetration evacuation policy already registered.");
            if(_hook==null)
            {
                var h=new HarmonyLib.Harmony("M8LOSATM2.PenetrationEvacuation");
                try
                {
                    _setPanicked=(Action<CrewManager,float>)Delegate.CreateDelegate(typeof(Action<CrewManager,float>),Panic);
                    h.Patch(Target,prefix:new HarmonyMethod(Prefix){priority=Priority.Last});
                }
                catch{h.Unpatch(Target,Prefix);_setPanicked=null;throw;}
                _hook=h;
            }
            Owners.Add(crew,new Entry{Owner=vehicle});
        }
        private static bool Filter(CrewManager __instance)
        {
            Entry entry;
            if(__instance==null||!Owners.TryGetValue(__instance,out entry)||entry.Owner==null||entry.Failed)return true;
            try
            {
                // The native handler first raises 60 s of crew panic, then
                // rolls/counts penetration events toward DoPanicEvacuation.
                // Retain its non-evacuation response; omit only the latter part.
                // No fragile one-time _canBailWhenPenetrated edit: native crew
                // incapacitation can otherwise re-enable that field later.
                _setPanicked(__instance,60f);
                return false;
            }
            catch(Exception e)
            {
                entry.Failed=true;MelonLogger.Warning("[M8 LOSAT] Penetration evacuation policy disabled safely: "+e.Message);
                return true;
            }
        }
        internal static void Unregister(Vehicle vehicle)
        {
            if(ReferenceEquals(vehicle,null))return;
            var remove=new List<CrewManager>();
            foreach(var pair in Owners)if(ReferenceEquals(pair.Value.Owner,vehicle))remove.Add(pair.Key);
            foreach(var key in remove)Owners.Remove(key);
            if(Owners.Count==0&&_hook!=null)
            {try{_hook.Unpatch(Target,Prefix);}finally{_hook=null;_setPanicked=null;}}
        }
    }
}
