using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(M8LOSATM2.Mod), "M8 LOSAT", "0.1.69-impact-audio-restored", "AIM")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace M8LOSATM2
{
    public sealed class Mod : MelonMod
    {
        internal const string Prefix = "[M8 LOSAT M2] ";
        private readonly HashSet<int> _converted = new HashSet<int>();
        private readonly HashSet<int> _attempted = new HashSet<int>();
        private readonly HashSet<int> _reportedCandidates = new HashSet<int>();
        private readonly Dictionary<int, string> _reportedWaitReasons = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _readyPasses = new Dictionary<int, int>();
        private readonly List<M2Conversion> _instances = new List<M2Conversion>();
        private float _nextDiscovery;
        private string _assetDirectory;
        private LosatCore.LosatService _losat;

        public override void OnInitializeMelon()
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(Mod).Assembly.Location);
            _assetDirectory = Path.Combine(assemblyDirectory ?? string.Empty, "M8LOSATAssets");
            _losat=new LosatCore.LosatService(HarmonyInstance);
            try{_losat.OnInitializeMelon();}
            catch(Exception error)
            {
                MelonLogger.Error(Prefix+"fire control initialization failed; conversion remains disabled: "+error);
                try{_losat.Shutdown();}catch(Exception cleanup){MelonLogger.Warning(Prefix+cleanup.Message);}
                _losat=null;return;
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            foreach (M2Conversion conversion in _instances) conversion.Dispose();
            _instances.Clear();
            _converted.Clear();
            _attempted.Clear();
            _reportedCandidates.Clear();
            _reportedWaitReasons.Clear();
            _readyPasses.Clear();
            _nextDiscovery = 0f;
            if(_losat!=null)_losat.OnSceneWasLoaded(buildIndex,sceneName);
        }

        public override void OnUpdate()
        {
            if(_losat==null)return;
            _losat.OnUpdate();
            if (Time.unscaledTime < _nextDiscovery) return;
            _nextDiscovery = Time.unscaledTime + 1.5f;
            try
            {
                foreach (Vehicle vehicle in Resources.FindObjectsOfTypeAll<Vehicle>())
                {
                    if (!IsStockM2(vehicle)) continue;
                    int id = vehicle.GetInstanceID();
                    if (_converted.Contains(id) || _attempted.Contains(id)) continue;
                    if (_reportedCandidates.Add(id))
                    {
                        // Routine discovery message disabled; readiness checks remain.
                    }

                    string reason;
                    if (!M2Conversion.IsReady(vehicle, out reason))
                    {
                        _readyPasses.Remove(id);
                        string previous;
                        if (!_reportedWaitReasons.TryGetValue(id, out previous) || previous != reason)
                        {
                            _reportedWaitReasons[id] = reason;
                            MelonLogger.Warning(Prefix + "waiting for stock M2 visual bindings: " + reason);
                        }
                        continue;
                    }
                    _reportedWaitReasons.Remove(id);

                    int passes;
                    _readyPasses.TryGetValue(id, out passes);
                    passes++;
                    _readyPasses[id] = passes;
                    if (passes < 2) continue;

                    // From this point onward the conversion can allocate meshes and decode textures.
                    // Attempt it only once per spawned vehicle so a permanent binding failure cannot
                    // reload the 61 MB asset package every discovery interval.
                    _attempted.Add(id);
                    _readyPasses.Remove(id);
                    M2Conversion conversion;
                    if (!M2Conversion.TryCreate(vehicle, _assetDirectory, _losat, out conversion))
                    {
                        MelonLogger.Error(Prefix + "conversion disabled for this M2 instance after one failed heavy attempt.");
                        break;
                    }
                    _converted.Add(id);
                    _instances.Add(conversion);
                    break;
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error(Prefix + "bounded discovery failed: " + exception);
            }
        }

        public override void OnLateUpdate()
        {
            for (int index = _instances.Count - 1; index >= 0; index--)
            {
                M2Conversion conversion = _instances[index];
                if (!conversion.IsAlive)
                {
                    conversion.Dispose();
                    _instances.RemoveAt(index);
                    continue;
                }
                conversion.UpdateBindings();
            }
            if(_losat!=null)_losat.OnLateUpdate();
        }

        public override void OnGUI(){if(_losat!=null)_losat.OnGUI();}
        public override void OnDeinitializeMelon()
        {
            foreach(var conversion in _instances)conversion.Dispose();_instances.Clear();
            if(_losat!=null)_losat.Shutdown();
        }

        private static bool IsStockM2(Vehicle vehicle)
        {
            if (vehicle == null || vehicle.gameObject == null ||
                !vehicle.gameObject.scene.IsValid() || !vehicle.gameObject.activeInHierarchy) return false;
            Unit unit = vehicle.BaseUnit;
            string identity = Normalize((unit == null ? string.Empty : unit.UniqueName) + " " +
                                        (unit == null ? string.Empty : unit.FriendlyName) + " " + vehicle.name);
            return identity.Contains("M2BRADLEY") && !identity.Contains("M8LOSAT");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("_", string.Empty)
                .Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        }
    }
}
