namespace M8LOSATM2.LosatCore
{
    internal static class DiscoveryBudget
    {
        internal const int VehiclesPerFrame=8,ArmorPerFrame=64;
        internal const float CachedPassSeconds=3f,ArmorFallbackSeconds=30f;
        internal static float VehicleFallbackSeconds(float sceneAge,bool hooked)
        {return sceneAge<15f ? .75f : hooked ? 15f : 3f;}
    }
}
