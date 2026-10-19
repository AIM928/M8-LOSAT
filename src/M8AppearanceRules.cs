using System;
using UnityEngine;
namespace M8LOSATM2
{
    internal static class M8AppearanceRules
    {
        internal static bool PaintedAlbedo(string relative)
        {
            if(string.IsNullOrEmpty(relative))return false;
            string file=System.IO.Path.GetFileName(relative.Replace('\\','/'));
            return file.EndsWith("_BaseColorAO.png",StringComparison.OrdinalIgnoreCase)&&
                (file.StartsWith("m2_",StringComparison.Ordinal)||file.StartsWith("m3_",StringComparison.Ordinal));
        }
        internal static Color32 DarkerPaint(Color32 value)
        {
            value.r=(byte)((value.r*85+50)/100);value.g=(byte)((value.g*85+50)/100);value.b=(byte)((value.b*85+50)/100);
            return value; // Preserve alpha/cutouts exactly; source PNGs untouched.
        }
        internal static bool OpticGlass(Vector3 a,Vector3 b,Vector3 c,Vector2 u,Vector2 v,Vector2 w)
        {
            Vector3 center=(a+b+c)/3f;
            if(Math.Abs(center.x)>.24f||center.y<.49f||center.y>.89f||center.z<1.67f||center.z>1.81f)return false;
            if(!GlassUv(u)||!GlassUv(v)||!GlassUv(w))return false;
            Vector3 n=Vector3.Cross(b-a,c-a).normalized;
            return Math.Abs(n.x)<.04f&&n.y>.24f&&n.y<.34f&&n.z>.94f;
        }
        private static bool GlassUv(Vector2 uv){return uv.x>=.51f&&uv.x<=.68f&&uv.y>=.14f&&uv.y<=.281f;}
    }
}
