namespace M8LOSATM2.LosatCore
{
    internal sealed class ReloadSoundSequence
    {
        private bool _active;
        private int _played;
        internal void Begin() {if(_active) return;_active=true;_played=1;}
        internal bool Advance(bool loading,bool paused,float elapsed,float total)
        {
            if(!loading) {_active=false;_played=0;return false;}
            if(!_active || paused || total<=0f || elapsed<0f || _played>=4 ||
                float.IsNaN(total) || float.IsInfinity(total) || float.IsNaN(elapsed) || float.IsInfinity(elapsed)) return false;
            if(elapsed<total*_played/4f) return false;
            // Never queue an unbounded audio burst after a hitch.
            int quarter=(int)(elapsed*4f/total);
            _played=System.Math.Min(4,System.Math.Max(_played+1,quarter+1));
            return true;
        }
    }
}
