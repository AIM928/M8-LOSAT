using System;
using System.Reflection;
using GHPC.Camera;

namespace M8LOSATM2.LosatCore
{
    // GPS and backup share the native FLIR slot. Awake runs for both and the
    // last one overwrites LinkedDaySight. Repair BOTH directions reversibly.
    internal sealed class M8SightPair : IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly FieldInfo DayField=typeof(CameraSlot).GetField("<LinkedDaySight>k__BackingField",Flags);
        private static readonly FieldInfo LinkedField=typeof(CameraSlot).GetField("<IsLinkedNightSight>k__BackingField",Flags);
        private CameraSlot _day,_night,_oldDay,_oldNight;private bool _oldLinked,_disposed;
        internal static M8SightPair Apply(CameraSlot day,CameraSlot night)
        {
            if(day==null||night==null||day==night||DayField==null||LinkedField==null)throw new InvalidOperationException("Native M8 day/night linkage unavailable.");
            var binding=new M8SightPair{_day=day,_night=night,_oldDay=night.LinkedDaySight,_oldNight=day.LinkedNightSight,_oldLinked=night.IsLinkedNightSight};
            try{day.LinkedNightSight=night;DayField.SetValue(night,day);LinkedField.SetValue(night,true);return binding;}
            catch{binding.Dispose();throw;}
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(_day!=null)_day.LinkedNightSight=_oldNight;
            if(_night!=null){DayField.SetValue(_night,_oldDay);LinkedField.SetValue(_night,_oldLinked);}
        }
    }
}
