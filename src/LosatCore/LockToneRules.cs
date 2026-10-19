using System;
using System.IO;

namespace M8LOSATM2.LosatCore
{
    internal static class LockToneRules
    {
        internal static byte[] BuildWave()
        {
            const int rate=22050,samples=2646; // 120 ms, mono 16-bit PCM.
            using(var stream=new MemoryStream(44+samples*2))
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(new byte[] {82,73,70,70});writer.Write(36+samples*2);
                writer.Write(new byte[] {87,65,86,69,102,109,116,32});writer.Write(16);
                writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);
                writer.Write((short)2);writer.Write((short)16);
                writer.Write(new byte[] {100,97,116,97});writer.Write(samples*2);
                for(int i=0;i<samples;i++)
                {
                    double envelope=Math.Sin(Math.PI*i/(samples-1));
                    writer.Write((short)(Math.Sin(2*Math.PI*1100*i/rate)*envelope*12000));
                }
                return stream.ToArray();
            }
        }
    }
}
