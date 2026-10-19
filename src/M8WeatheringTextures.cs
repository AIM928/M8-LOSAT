using System;
using System.IO;
namespace M8LOSATM2
{
    // Offline-baked original-UV albedos only; no image processing during loading
    // or gameplay, no change to external source PNGs/normal/finish resources.
    internal static class M8WeatheringTextures
    {
        internal static string ResourceName(string relative)
        {
            if(string.IsNullOrEmpty(relative))return null;
            string file=Path.GetFileName(relative.Replace('\\','/'));
            switch(file)
            {
                case "m0_Material_001_BaseColorAO.png":return "M8LOSATM2.Weathering.m0";
                case "m1_履带_001_BaseColorAO.png":return "M8LOSATM2.Weathering.m1";
                case "m2_炮塔_001_BaseColorAO.png":return "M8LOSATM2.Weathering.m2";
                case "m3_车体_001_BaseColorAO.png":return "M8LOSATM2.Weathering.m3";
                default:return null;
            }
        }
        internal static byte[] ReadAlbedo(string relative)
        {
            string name=ResourceName(relative);if(name==null)return null;
            using(var stream=typeof(M8WeatheringTextures).Assembly.GetManifestResourceStream(name))
            {
                if(stream==null||stream.Length<8||stream.Length>64000000)throw new InvalidDataException("M8 baked albedo missing or oversized: "+name);
                var bytes=new byte[(int)stream.Length];int read=0;
                while(read<bytes.Length){int count=stream.Read(bytes,read,bytes.Length-read);if(count==0)throw new EndOfStreamException("M8 baked albedo truncated");read+=count;}
                return bytes;
            }
        }
    }
}
