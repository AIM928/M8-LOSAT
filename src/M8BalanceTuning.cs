using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Vehicle;
using NWH.VehiclePhysics;
using UnityEngine;

namespace M8LOSATM2
{
    // Instance-local balance and minimum turret plate thickness, never global.
    internal sealed class M8BalanceTuning : IDisposable
    {
        internal const float SpallScale=.75f,PowerScale=1.30f;
        internal const float MinimumTurretThickness=12f;
        internal const float ReverseSpeedKph=25f,ReverseSpeedMs=ReverseSpeedKph/3.6f;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static readonly FieldInfo ArmorField=typeof(UniformArmor).GetField("_armorType",Flags);
        private static readonly FieldInfo PowerField=typeof(NwhChassis).GetField("_originalEnginePower",Flags);
        private static readonly FieldInfo ReverseLimitField=typeof(NwhChassis).GetField("_maxReverseSpeed",Flags);
        private static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",Flags);
        private static readonly HashSet<Vehicle> Active=new HashSet<Vehicle>();
        private readonly Dictionary<UniformArmor,ArmorCodexScriptable> _originals=new Dictionary<UniformArmor,ArmorCodexScriptable>();
        private readonly Dictionary<ArmorCodexScriptable,ArmorCodexScriptable> _copies=new Dictionary<ArmorCodexScriptable,ArmorCodexScriptable>();
        private readonly Dictionary<UniformArmor,float[]> _thickness=new Dictionary<UniformArmor,float[]>();
        private Vehicle _vehicle;private NwhChassis _chassis;private Engine _engine;
        private float _originalPower;private bool _powerChanged,_disposed;
        private Transmission _transmission;private List<float> _originalReverseGears;private bool _reverseChanged;
        private float _originalReverseLimit;private bool _reverseLimitChanged;

        internal static bool IsReady(Vehicle vehicle,out string reason)
        {
            reason="";
            if(ArmorField==null||PowerField==null||ReverseLimitField==null||Clone==null||typeof(Transmission).GetField("reverseGears",Flags)==null){reason="M8 balance native contract unavailable.";return false;}
            var controller=vehicle==null?null:vehicle.VehicleController;
            var chassis=vehicle==null?null:vehicle.GetComponent<NwhChassis>();
            if(controller==null||!controller.IsInitialized||controller.engine==null||chassis==null||chassis.VehicleController!=controller)
            {reason="Waiting for initialized M8 powertrain.";return false;}
            if(!Positive(controller.engine.maxPower)||!Positive((float)PowerField.GetValue(chassis)))
            {reason="Native M8 engine power unavailable.";return false;}
            if(!Positive((float)ReverseLimitField.GetValue(chassis)))
            {reason="Native M8 reverse speed limit unavailable.";return false;}
            if(controller.transmission==null||controller.transmission.ReverseGears==null||controller.transmission.ReverseGears.Count==0)
            {reason="Native M8 reverse gearing unavailable.";return false;}
            foreach(float ratio in controller.transmission.ReverseGears)if(!Positive(Math.Abs(ratio)))
            {reason="Invalid native M8 reverse gear ratio.";return false;}
            if(!Positive(controller.AvgWheelRadius)||!Positive(controller.transmission.gearMultiplier)||
                !Positive(controller.engine.maxRPM)||!Positive(controller.transmission.targetShiftUpRPM))
            {reason="Waiting for initialized M8 reverse calibration data.";return false;}
            return true;
        }
        private static bool Positive(float value){return value>0&&!float.IsNaN(value)&&!float.IsInfinity(value);}
        internal static M8BalanceTuning Apply(Vehicle vehicle)
        {
            string reason;if(!IsReady(vehicle,out reason))throw new InvalidOperationException(reason);
            if(!Active.Add(vehicle))throw new InvalidOperationException("M8 balance already installed on this instance.");
            var tuning=new M8BalanceTuning{_vehicle=vehicle};
            try{tuning.Install();return tuning;}catch{tuning.Dispose();throw;}
        }
        private void Install()
        {
            foreach(var armor in M8ArmorDiscovery.FindOwned(_vehicle))
            {
                // Native armor rigs detach at runtime; discovery verifies cached
                // vehicle ownership. Only exterior Hull/Turret plates are tuned.
                if(!armor.gameObject.activeSelf||(!armor.name.StartsWith("Hull ",StringComparison.Ordinal)&&!armor.name.StartsWith("Turret ",StringComparison.Ordinal)))continue;
                var original=armor.ArmorType;
                if(original==null||original.ArmorType==null)throw new InvalidOperationException("M8 exterior armor material missing: "+armor.name);
                float power=original.ArmorType.SpallPowerMultiplier;
                if(float.IsNaN(power)||float.IsInfinity(power)||power<0)throw new InvalidOperationException("Invalid native spall coefficient.");
                ArmorCodexScriptable copy;
                if(!_copies.TryGetValue(original,out copy))
                {
                    copy=UnityEngine.Object.Instantiate(original);_copies.Add(original,copy);
                    // ArmorType is a reference class, not a value type. Never
                    // mutate the shared source codex's nested data.
                    copy.ArmorType=(GHPC.Equipment.ArmorType)Clone.Invoke(original.ArmorType,null);
                    copy.ArmorType.SpallPowerMultiplier=power*SpallScale;
                }
                _originals.Add(armor,original);ArmorField.SetValue(armor,copy);
                if(armor.name.StartsWith("Turret ",StringComparison.Ordinal))
                {
                    // Preserve native ThicknessListed/material interpretation.
                    // This is not a claim that aluminum millimeters equal steel.
                    if(!Positive(armor.PrimarySabotRha)||!Positive(armor.PrimaryHeatRha))throw new InvalidOperationException("Invalid turret plate thickness: "+armor.name);
                    _thickness.Add(armor,new[]{armor.PrimarySabotRha,armor.PrimaryHeatRha});
                    armor.PrimarySabotRha=Math.Max(MinimumTurretThickness,armor.PrimarySabotRha);
                    armor.PrimaryHeatRha=Math.Max(MinimumTurretThickness,armor.PrimaryHeatRha);
                }
            }
            if(_originals.Count==0)throw new InvalidOperationException("No owned exterior M8 armor available for spall tuning.");
            _chassis=_vehicle.GetComponent<NwhChassis>();_engine=_vehicle.VehicleController.engine;
            _originalReverseLimit=(float)ReverseLimitField.GetValue(_chassis);
            // The chassis cap is only a driver request, not a gearing speed.
            ReverseLimitField.SetValue(_chassis,ReverseSpeedMs);_reverseLimitChanged=true;
            _transmission=_vehicle.VehicleController.transmission;_originalReverseGears=_transmission.ReverseGears;
            // Read INITIALIZED native radius/final drive: tracked initialization
            // enlarges both by the same factor. Never use visual wheel radius or
            // infer a mechanical speed from the unrelated old chassis cap.
            float calibrationRpm=Math.Min(_engine.maxRPM*.9f,_transmission.targetShiftUpRPM);
            float fastestRatio=float.PositiveInfinity;
            foreach(float ratio in _originalReverseGears)fastestRatio=Math.Min(fastestRatio,Math.Abs(ratio));
            float targetRatio=_vehicle.VehicleController.AvgWheelRadius*calibrationRpm*(2f*(float)Math.PI/60f)/
                (_transmission.gearMultiplier*ReverseSpeedMs);
            float scale=targetRatio/fastestRatio;
            if(!Positive(scale))throw new InvalidOperationException("Invalid M8 reverse calibration scale.");
            var reverse=new List<float>(_originalReverseGears.Count);
            foreach(float ratio in _originalReverseGears)reverse.Add(ratio*scale);
            // Native ReconstructGearList reverses its input list IN PLACE.
            // Pre-reverse our private copy so the active reverse order stays
            // identical to the already initialized original transmission.
            reverse.Reverse();
            _reverseChanged=true;_transmission.ReverseGears=reverse;
            _originalPower=(float)PowerField.GetValue(_chassis);
            PowerField.SetValue(_chassis,_originalPower*PowerScale);
            _powerChanged=true;_engine.maxPower*=PowerScale;
            // SetEnginePowerPercent uses the cached baseline after engine damage;
            // scaling both retains its current damage ratio and future derating.
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(_reverseLimitChanged&&_chassis!=null)ReverseLimitField.SetValue(_chassis,_originalReverseLimit);
            if(_reverseChanged&&_transmission!=null)
            {
                var restore=new List<float>(_originalReverseGears);restore.Reverse();
                _transmission.ReverseGears=restore;
                // Active gears now contain the original ordered values. Put
                // back the original list reference without ever reversing it.
                typeof(Transmission).GetField("reverseGears",Flags).SetValue(_transmission,_originalReverseGears);
            }
            if(_powerChanged)
            {
                if(_chassis!=null)PowerField.SetValue(_chassis,_originalPower);
                if(_engine!=null)_engine.maxPower/=PowerScale;
            }
            foreach(var pair in _originals)if(pair.Key!=null)ArmorField.SetValue(pair.Key,pair.Value);
            foreach(var pair in _thickness)if(pair.Key!=null){pair.Key.PrimarySabotRha=pair.Value[0];pair.Key.PrimaryHeatRha=pair.Value[1];}
            foreach(var copy in _copies.Values)if(copy!=null)UnityEngine.Object.Destroy(copy);
            _originals.Clear();_copies.Clear();_thickness.Clear();if(!ReferenceEquals(_vehicle,null))Active.Remove(_vehicle);
        }
    }
}
