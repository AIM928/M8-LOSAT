using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Effects.Voices;
using GHPC.Vehicle;
using HarmonyLib;
using MelonLoader;

namespace M8LOSATM2
{
    // Filter requests, never mute a shared mixer, edit a native voice asset,
    // disable a crew brain or leave a speech coroutine waiting forever.
    internal static class M8CrewVoicePolicy
    {
        private sealed class Entry
        {
            internal Vehicle Owner;internal VoiceProtocolDataScriptable Protocol;
            internal readonly HashSet<string> Allowed=new HashSet<string>(StringComparer.Ordinal);
            internal bool Failed;
        }
        private static readonly Dictionary<CrewVoiceHandler,Entry> Owners=new Dictionary<CrewVoiceHandler,Entry>();
        private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly FieldInfo ProtocolField=typeof(CrewVoiceHandler).GetField("_voiceProtocolLineData",Flags);
        private static readonly FieldInfo AmmoKeys=typeof(VoiceProtocolDataScriptable).GetField("AmmoTypeToStringDict",Flags);
        private static readonly MethodInfo Prefix=typeof(M8CrewVoicePolicy).GetMethod("Filter",BindingFlags.Static|BindingFlags.NonPublic);
        private static readonly MethodInfo[] Targets={
            typeof(CrewVoiceHandler).GetMethod("PlayVoiceLine",Flags,null,new[]{typeof(string),typeof(GHPC.Crew.CrewPosition),typeof(bool),typeof(float),typeof(bool),typeof(bool)},null),
            typeof(CrewVoiceHandler).GetMethod("PlayVoiceLineAtAnyFreePosition",Flags,null,new[]{typeof(string),typeof(bool),typeof(bool),typeof(float),typeof(bool),typeof(bool)},null)};
        private static HarmonyLib.Harmony _hook;
        internal static void Register(Vehicle vehicle)
        {
            if(vehicle==null||ProtocolField==null||AmmoKeys==null||Prefix==null)throw new InvalidOperationException("Native crew voice contract unavailable.");
            foreach(var target in Targets)if(target==null)throw new MissingMethodException("Native voice request method unavailable.");
            var handlers=vehicle.GetComponentsInChildren<CrewVoiceHandler>(true);
            if(handlers.Length!=1||!handlers[0].transform.IsChildOf(vehicle.transform)&&handlers[0].transform!=vehicle.transform)
                throw new InvalidOperationException("Expected one owned M8 crew voice handler.");
            if(Owners.ContainsKey(handlers[0]))throw new InvalidOperationException("M8 voice policy already registered.");
            if(_hook==null)
            {
                var h=new HarmonyLib.Harmony("M8LOSATM2.MissileCrewVoices");
                try{foreach(var target in Targets)h.Patch(target,prefix:new HarmonyMethod(Prefix){priority=Priority.Last});}
                catch{foreach(var target in Targets)h.Unpatch(target,Prefix);throw;}
                _hook=h;
            }
            Owners.Add(handlers[0],new Entry{Owner=vehicle});
        }
        private static void Add(Entry entry,string key){if(!string.IsNullOrEmpty(key))entry.Allowed.Add(key);}
        private static bool Allowed(Entry entry,CrewVoiceHandler handler,string key)
        {
            // All inherited M2 crew speech is suppressed for registered M8s,
            // including missile calls. Completed receipts still release native
            // speech tasks. Weapon/flight/lock/mechanical audio is unrelated.
            return false;
        }
        private static bool Filter(CrewVoiceHandler __instance,string __0,ref VoiceLineReceipt __result)
        {
            Entry entry;if(__instance==null||!Owners.TryGetValue(__instance,out entry)||entry.Owner==null||entry.Failed)return true;
            try
            {
                if(Allowed(entry,__instance,__0))return true;
                // Same public completed receipt used by native routines for a
                // skipped voice: no audio and no stalled task/coroutine.
                __result=VoiceLineReceipt.CompletedReceipt;return false;
            }
            catch(Exception e)
            {
                entry.Failed=true;MelonLogger.Warning("[M8 LOSAT] Crew voice filter disabled safely: "+e.Message);return true;
            }
        }
        internal static void Unregister(Vehicle vehicle)
        {
            // Unity's destroyed objects compare equal to null before their CLR
            // references disappear. Cleanup must still remove that exact owner.
            if(ReferenceEquals(vehicle,null))return;
            var remove=new List<CrewVoiceHandler>();foreach(var pair in Owners)if(ReferenceEquals(pair.Value.Owner,vehicle))remove.Add(pair.Key);
            foreach(var key in remove)Owners.Remove(key);
            if(Owners.Count==0&&_hook!=null)
            {try{foreach(var target in Targets)_hook.Unpatch(target,Prefix);}finally{_hook=null;}}
        }
    }
}
