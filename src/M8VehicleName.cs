using System;
using System.Reflection;
using GHPC;

namespace M8LOSATM2
{
    // Per converted runtime instance only. Never rename the shared template or
    // mission identity, and do not overwrite a later name supplied by another mod.
    internal sealed class M8VehicleName : IDisposable
    {
        internal const string DisplayName="M8 LOSAT";
        private static readonly FieldInfo FriendlyField=typeof(Unit).GetField("_friendlyName",BindingFlags.Instance|BindingFlags.NonPublic);
        private Unit _unit;
        private readonly string _before;
        internal M8VehicleName(Unit unit)
        {
            if(unit==null || FriendlyField==null || FriendlyField.FieldType!=typeof(string) || FriendlyField.IsInitOnly)
                throw new InvalidOperationException("Native per-instance friendly-name contract unavailable.");
            _before=(string)FriendlyField.GetValue(unit);
            FriendlyField.SetValue(unit,DisplayName);
            _unit=unit;
        }
        public void Dispose()
        {
            Unit unit=_unit;_unit=null;
            if(unit!=null && string.Equals((string)FriendlyField.GetValue(unit),DisplayName,StringComparison.Ordinal))
                FriendlyField.SetValue(unit,_before);
        }
    }
}
