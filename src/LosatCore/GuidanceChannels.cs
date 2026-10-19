namespace M8LOSATM2.LosatCore
{
    internal sealed class GuidanceChannels
    {
        internal const int Capacity=3;
        private readonly int[] _ids=new int[Capacity];
        private readonly bool[] _used=new bool[Capacity];
        internal int Count {get;private set;}
        internal int Available {get {return Capacity-Count;}}
        internal bool Register(int id)
        {
            for(int i=0;i<Capacity;i++) if(_used[i] && _ids[i]==id) return true;
            for(int i=0;i<Capacity;i++) if(!_used[i]) {_used[i]=true;_ids[i]=id;Count++;return true;}
            return false;
        }
        internal void Release(int id)
        {for(int i=0;i<Capacity;i++) if(_used[i] && _ids[i]==id) {_used[i]=false;Count--;return;}}
    }
    internal static class GuidanceDisplayRules
    {
        internal static bool LampOn(bool ready,float time) {return ready || time-(int)time<.45f;}
    }
}
