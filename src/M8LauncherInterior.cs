using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace M8LOSATM2
{
    // Fixed visual supports only. Never join these nodes to Fired()'s tube list.
    internal static class M8LauncherInterior
    {
        private static bool[][][] _selection;
        internal static Color32 Black(Color32 p)
        {
            int luminance=(54*p.r+183*p.g+19*p.b)/256;
            byte shade=(byte)(3+luminance*16/100);
            return new Color32(shade,shade,shade,p.a);
        }
        internal static Material MakeMaterial(M8ModelInstance model,Material source,byte[] albedo)
        {
            if(model==null||source==null||albedo==null)throw new InvalidOperationException("Missing M8 interior surface.");
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false);model.Own(texture);
            if(!ImageConversion.LoadImage(texture,albedo,false))throw new InvalidDataException("M8 interior albedo decode failed.");
            var pixels=texture.GetPixels32();for(int i=0;i<pixels.Length;i++)pixels[i]=Black(pixels[i]);
            texture.SetPixels32(pixels);texture.name="M8_Interior_Black_Original_Detail";
            texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=4;texture.Apply(true,true);
            var material=new Material(source);model.Own(material);material.name="M8_Interior_Black_Metal";
            material.SetTexture("_Albedo",texture);material.SetTexture("_CamoLayer",texture);
            return material; // Shader, normal, smoothness and every float stay copied.
        }
        private static bool[][][] Selection()
        {
            if(_selection!=null)return _selection;
            using(var stream=typeof(M8LauncherInterior).Assembly.GetManifestResourceStream("M8LOSATM2.InteriorFrames"))
            {
                if(stream==null||stream.Length>200000)throw new InvalidDataException("Missing M8 fixed-frame table.");
                using(var r=new BinaryReader(stream))
                {
                    if(r.ReadInt32()!=838100||r.ReadInt32()!=1||r.ReadInt32()!=3)throw new InvalidDataException("Wrong frame table.");
                    var result=new bool[3][][];
                    for(int lod=0;lod<3;lod++)
                    {
                        int ns=r.ReadInt32();if(ns<1||ns>16)throw new InvalidDataException("Invalid frame submeshes.");result[lod]=new bool[ns][];
                        for(int s=0;s<ns;s++){int n=r.ReadInt32();if(n<0||n>50000)throw new InvalidDataException("Invalid frame count.");var flags=new bool[n];for(int i=0;i<n;i++){byte b=r.ReadByte();if(b>1)throw new InvalidDataException("Invalid frame flag.");flags[i]=b!=0;}result[lod][s]=flags;}
                    }
                    if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing frame data.");
                    _selection=result;return result;
                }
            }
        }
        internal static Renderer SplitFrame(M8ModelInstance model,Mesh body,Material black,GameObject parent,int lod)
        {
            if(lod<0||lod>2||body==null||black==null)throw new InvalidOperationException("Invalid M8 interior owner/LOD.");
            var selection=Selection()[lod];if(selection.Length!=body.subMeshCount)throw new InvalidDataException("Frame submesh mismatch.");
            var chosen=new List<int>();int[][] retained=new int[body.subMeshCount][];
            for(int s=0;s<body.subMeshCount;s++)
            {
                int[] indices=body.GetTriangles(s);if(indices.Length!=selection[s].Length*3)throw new InvalidDataException("Frame triangle mismatch.");
                var keep=new List<int>();for(int i=0;i<indices.Length;i+=3){var output=selection[s][i/3]?chosen:keep;output.Add(indices[i]);output.Add(indices[i+1]);output.Add(indices[i+2]);}retained[s]=keep.ToArray();
            }
            if(chosen.Count<600||chosen.Count>40000)throw new InvalidDataException("Missing/oversized fixed supports.");
            var mesh=new Mesh{name="M8_Black_Fixed_Supports_LOD"+lod,indexFormat=IndexFormat.UInt32};model.Own(mesh);
            mesh.vertices=body.vertices;mesh.normals=body.normals;mesh.uv=body.uv;mesh.triangles=chosen.ToArray();mesh.RecalculateTangents();mesh.RecalculateBounds();
            for(int s=0;s<retained.Length;s++)body.SetTriangles(retained[s],s);
            var node=new GameObject("M8_Black_Fixed_Supports_LOD"+lod);node.layer=parent.layer;node.transform.SetParent(parent.transform,false);
            node.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterial=black;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;return renderer;
        }
    }
}
