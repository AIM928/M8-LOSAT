namespace M8LOSATM2.LosatCore
{
    internal sealed class MissileTargetAssignment
    {
        internal readonly int RoundId;
        internal readonly int VehicleId;
        internal MissileTargetAssignment(int roundId,int vehicleId)
        {RoundId=roundId;VehicleId=vehicleId;}
        internal bool MatchesLiveRound(int currentId,bool pooled,bool destroyed)
        {return currentId==RoundId && !pooled && !destroyed;}
    }
}
