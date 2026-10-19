using System;
namespace M8LOSATM2.LosatCore
{
    internal static class LaunchDataRules
    {
        // Diagnostic flags only: never change ballistics, ammo or transforms.
        internal const int InvalidSpeed=1,UnexpectedMaximum=2,UnexpectedKe=4,SpawnMismatch=8;
        private static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        internal static int Check(float speed,float maximum,float ke,float spawnError,float expectedMaximum,float expectedKe)
        {
            int flags=0;
            if(!Finite(speed)||speed<=0f||speed>expectedMaximum+1f) flags|=InvalidSpeed;
            if(!Finite(maximum)||Math.Abs(maximum-expectedMaximum)>.5f) flags|=UnexpectedMaximum;
            if(!Finite(ke)||Math.Abs(ke-expectedKe)>.5f) flags|=UnexpectedKe;
            if(!Finite(spawnError)||spawnError<0f||spawnError>.05f) flags|=SpawnMismatch;
            return flags;
        }
    }
}
