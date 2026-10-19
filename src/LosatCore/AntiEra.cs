using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private readonly List<ArmorCodexScriptable> _eraCatalog=new List<ArmorCodexScriptable>();
        private readonly HashSet<ArmorCodexScriptable> _knownEra=new HashSet<ArmorCodexScriptable>();
        private ArmorCodexScriptable[] _eraSnapshot;
        private int _eraCursor;
        private float _nextEraFallback;
        private bool _eraChanged;
        private readonly List<AmmoType> _antiEraAmmo=new List<AmmoType>();
        private void RegisterAntiEraAmmo(AmmoType ammo)
        {
            if(!_antiEraAmmo.Contains(ammo)) _antiEraAmmo.Add(ammo);
            EraOptimizationRules.Apply(ammo,_eraCatalog);
        }
        private void RememberEraArmor(ArmorCodexScriptable armor)
        {
            if(armor==null || !EraOptimizationRules.RecognizedEra(armor.name) || !_knownEra.Add(armor)) return;
            _eraCatalog.Add(armor);_eraChanged=true;
        }
        private void RefreshEraArmorCatalog(bool allowSceneScan)
        {
            if(_antiEraAmmo.Count==0) return;
            if(_eraSnapshot==null && allowSceneScan && Time.unscaledTime>=_nextEraFallback) {
                _nextEraFallback=Time.unscaledTime+DiscoveryBudget.ArmorFallbackSeconds;
                _eraSnapshot=Resources.FindObjectsOfTypeAll<ArmorCodexScriptable>();_eraCursor=0;
            }
            if(_eraSnapshot!=null) {
                int end=System.Math.Min(_eraSnapshot.Length,_eraCursor+DiscoveryBudget.ArmorPerFrame);
                while(_eraCursor<end) RememberEraArmor(_eraSnapshot[_eraCursor++]);
                if(_eraCursor==_eraSnapshot.Length) _eraSnapshot=null;
            }
            // Do not clone private optimization arrays once per armor component.
            // Apply once after a fallback batch completes, or after spawn notifications.
            if(!_eraChanged || _eraSnapshot!=null) return;
            _eraChanged=false;
            foreach(AmmoType ammo in _antiEraAmmo) EraOptimizationRules.Apply(ammo,_eraCatalog);
            foreach(TrackedMissile tracked in _missiles)
                if(tracked.Round!=null && !tracked.Round.Pooled && !tracked.Round.IsDestroyed && tracked.Round.ID==tracked.RoundId)
                    EraOptimizationRules.Apply(tracked.Ammo,_eraCatalog);
        }
        private void ResetAntiEra()
        {_antiEraAmmo.Clear();_eraCatalog.Clear();_knownEra.Clear();_eraSnapshot=null;_eraCursor=0;_nextEraFallback=0f;_eraChanged=false;}
    }
}
