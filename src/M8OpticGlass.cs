using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace M8LOSATM2
{
    // Isolate the actual front glass only. The dirty paint atlas/normal map is
    // not reused on this surface; frame, tubes, shell and all source files stay.
    internal sealed class M8OpticGlass
    {
        private Material _material;
        private static Texture2D Solid(M8ModelInstance model,Color32 value,bool linear,string name)
        {
            var t=new Texture2D(1,1,TextureFormat.RGBA32,false,linear){name=name};model.Own(t);
            t.SetPixels32(new[]{value});t.Apply(false,true);return t;
        }
        internal Renderer Split(M8ModelInstance model,Mesh body,Material[] materials,GameObject parent,int lod)
        {
            var p=body.vertices;var n=body.normals;var uv=body.uv;var glass=new List<int>();
            var retained=new int[body.subMeshCount][];
            for(int sub=0;sub<body.subMeshCount;sub++)
            {
                var keep=new List<int>();var indices=body.GetTriangles(sub);
                for(int i=0;i<indices.Length;i+=3)
                {
                    int a=indices[i],b=indices[i+1],c=indices[i+2];
                    var output=M8AppearanceRules.OpticGlass(p[a],p[b],p[c],uv[a],uv[b],uv[c])?glass:keep;
                    output.Add(a);output.Add(b);output.Add(c);
                }
                retained[sub]=keep.ToArray();
            }
            if(glass.Count!=6)throw new InvalidOperationException("M8 front optic glass topology differs: LOD="+lod+" triangles="+glass.Count/3);
            if(_material==null)
            {
                _material=new Material(materials[0]){name="M8_Clean_Optic_Glass"};model.Own(_material);
                M8MaterialRules.ApplyNativeBaseline(_material,
                    Solid(model,new Color32(23,34,38,255),false,"M8_Clean_Glass_Albedo"),
                    Solid(model,new Color32(128,128,255,255),true,"M8_Clean_Glass_Normal"),
                    Solid(model,new Color32(20,20,20,190),true,"M8_Clean_Glass_Finish"),false,0f);
            }
            var lookup=new Dictionary<int,int>();var positions=new List<Vector3>();var normals=new List<Vector3>();var uvs=new List<Vector2>();
            for(int i=0;i<glass.Count;i++)
            {
                int id=glass[i],mapped;if(!lookup.TryGetValue(id,out mapped))
                {mapped=positions.Count;lookup.Add(id,mapped);positions.Add(p[id]);normals.Add(n[id]);uvs.Add(uv[id]);}
                glass[i]=mapped;
            }
            var mesh=new Mesh{name="M8_Clean_Optic_Glass_LOD"+lod};model.Own(mesh);
            mesh.vertices=positions.ToArray();mesh.normals=normals.ToArray();mesh.uv=uvs.ToArray();mesh.triangles=glass.ToArray();mesh.RecalculateTangents();mesh.RecalculateBounds();
            var go=new GameObject("M8_Clean_Optic_Glass_LOD"+lod);go.layer=parent.layer;go.transform.SetParent(parent.transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=_material;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
            for(int sub=0;sub<body.subMeshCount;sub++)body.SetTriangles(retained[sub],sub);
            return renderer;
        }
    }
}
