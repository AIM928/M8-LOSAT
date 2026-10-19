using System;
namespace M8LOSATM2.LosatCore
{
    internal static class SectorPresentationRules
    {
        internal static int ThermalWidth(int zoom)
        {return zoom<=0 ? 1920 : zoom==1 ? 1440 : zoom==2 ? 960 : 640;}
        internal static int ThermalHeight(int zoom)
        {return zoom<=0 ? 1080 : zoom==1 ? 810 : zoom==2 ? 540 : 360;}
        // Hull-local +X is right: turn the hull toward a positive bearing.
        internal static int TurnDirection(float bearing)
        {return FixedLauncherRules.Allowed(bearing) || float.IsNaN(bearing) || float.IsInfinity(bearing) ? 0 : bearing>0 ? 1 : -1;}
    }
    internal sealed class SectorVoiceTiming
    {
        private int _direction,_spokenDirection;
        private float _since,_lastSpeech=-100f;
        internal int Poll(int direction,float now,bool ready)
        {
            if(!ready || direction==0) {_direction=0;return 0;}
            if(direction!=_direction) {_direction=direction;_since=now;return 0;}
            if(now-_since<.35f || now-_lastSpeech<1.8f ||
                (direction==_spokenDirection && now-_lastSpeech<6f)) return 0;
            _lastSpeech=now;_spokenDirection=direction;return direction;
        }
    }
}
