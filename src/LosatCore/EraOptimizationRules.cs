using System;
using System.Collections.Generic;

namespace M8LOSATM2.LosatCore
{
    internal static class EraOptimizationRules
    {
        internal const float ResistanceRatio=.25f; // Installed M829A3's KE anti-ERA value.
        internal static bool RecognizedEra(string name)
        {
            if(string.IsNullOrEmpty(name)) return false;
            // Dedicated codices used by the installed M829A3 / TOW2A code.
            return name.IndexOf("Kontakt-1",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("Kontakt-5",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("Relikt",StringComparison.OrdinalIgnoreCase)>=0 ||
                name.IndexOf("M1 ERA",StringComparison.OrdinalIgnoreCase)>=0;
        }
        internal static void Apply(AmmoType ammo,IList<ArmorCodexScriptable> catalog)
        {
            if(ammo==null || catalog==null || catalog.Count==0) return;
            // Clone the list, never edit an array inherited from another ammo type.
            var rules=ammo.ArmorOptimizations==null ? new List<AmmoType.ArmorOptimization>() :
                new List<AmmoType.ArmorOptimization>(ammo.ArmorOptimizations);
            bool changed=false;
            foreach(ArmorCodexScriptable armor in catalog)
            {
                if(armor==null || !RecognizedEra(armor.name)) continue;
                bool found=false;
                for(int i=0;i<rules.Count;i++)
                {
                    if(rules[i].Armor!=armor) continue;
                    found=true;
                    AmmoType.ArmorOptimization rule=rules[i];
                    if(float.IsNaN(rule.RhaRatio) || float.IsInfinity(rule.RhaRatio) || rule.RhaRatio<0f || rule.RhaRatio>ResistanceRatio)
                    {rule.RhaRatio=ResistanceRatio;rules[i]=rule;changed=true;}
                }
                if(!found) {rules.Add(new AmmoType.ArmorOptimization {Armor=armor,RhaRatio=ResistanceRatio});changed=true;}
            }
            if(changed) ammo.ArmorOptimizations=rules.ToArray();
        }
    }
}
