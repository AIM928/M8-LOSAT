using System;
namespace M8LOSATM2.LosatCore
{
    // A bounded one-click transaction, not a held-trigger/autofire loop.
    internal sealed class TargetSalvoPlan
    {
        internal const float TargetTimeout=3f;
        internal const int MaximumShots=GuidanceChannels.Capacity;
        private readonly int[] _targets=new int[TargetLockRules.MaximumTargets];
        internal int Count {get;private set;}
        internal int Index {get;private set;}
        internal float NextAttempt {get;private set;}
        internal float Deadline {get;private set;}
        internal bool Pending {get {return Index<Count;}}
        internal int TargetId {get {return Pending ? _targets[Index] : -1;}}
        internal bool Begin(int[] ids,int ready,float now)
        {
            if(Pending || ids==null || ready<=0) return false;
            Count=0;Index=0;
            for(int i=0;i<ids.Length && Count<Math.Min(ready,MaximumShots);i++)
            {
                bool duplicate=false;
                for(int j=0;j<Count;j++) if(_targets[j]==ids[i]) duplicate=true;
                // Unity instance IDs are opaque signed integers, not array indices.
                if(!duplicate) _targets[Count++]=ids[i];
            }
            NextAttempt=now;Deadline=now+TargetTimeout;
            return Pending;
        }
        internal bool CanAttempt(float now) {return Pending && now>=NextAttempt && now<=Deadline;}
        internal bool Expired(float now) {return Pending && now>Deadline;}
        internal void SuccessfulShot(float now)
        {
            if(!Pending) return;
            Index++;NextAttempt=now+SalvoSequence.Interval;Deadline=NextAttempt+TargetTimeout;
        }
        internal void Skip(float now)
        {
            if(!Pending) return;
            Index++;NextAttempt=now;Deadline=now+TargetTimeout;
        }
        internal void Cancel() {Count=0;Index=0;}
    }
}
