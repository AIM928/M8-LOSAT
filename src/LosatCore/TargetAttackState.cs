namespace M8LOSATM2.LosatCore
{
    // One lock survives misses. Acknowledgements must match the exact launch ID.
    internal sealed class TargetAttackState
    {
        internal bool InFlight {get;private set;}
        internal bool Attempted {get;private set;}
        internal int RoundId {get;private set;}
        internal bool Begin(int round)
        {if(InFlight) return false;RoundId=round;InFlight=true;Attempted=true;return true;}
        internal bool ConfirmHit(int round,bool assignedVehicleHit)
        {if(!InFlight || RoundId!=round || !assignedVehicleHit) return false;InFlight=false;return true;}
        internal void EndWithoutHit(int round)
        {if(InFlight && RoundId==round) InFlight=false;}
    }
}
