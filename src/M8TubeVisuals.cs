using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
namespace M8LOSATM2
{
    internal sealed class M8TubeVisuals
    {
        private readonly List<GameObject>[] _tubes=new List<GameObject>[12];
        private readonly int[][][] _triangleTubes=new int[3][][];
        private int _loaded=4095;
        internal M8TubeVisuals(string assetDirectory)
        {
            for(int i=0;i<12;i++)_tubes[i]=new List<GameObject>(3);
            string path=Path.Combine(assetDirectory,"GHPC","M8Tubes.indicesbin");
            if(!File.Exists(path)||new FileInfo(path).Length>1000000)throw new InvalidDataException("M8 independent tube table missing or oversized.");
            using(var r=new BinaryReader(File.OpenRead(path)))
            {
                if(r.ReadInt32()!=814120||r.ReadInt32()!=1||r.ReadInt32()!=3)throw new InvalidDataException("Wrong M8 tube table.");
                for(int lod=0;lod<3;lod++)
                {
                    int n=r.ReadInt32();if(n<1||n>16)throw new InvalidDataException("Invalid tube submesh count.");
                    _triangleTubes[lod]=new int[n][];
                    for(int s=0;s<n;s++)
                    {
                        int count=r.ReadInt32();if(count<0||count>50000)throw new InvalidDataException("Invalid tube triangle count.");
                        var ids=new int[count];for(int i=0;i<count;i++){ids[i]=r.ReadInt32();if(ids[i]<-1||ids[i]>11)throw new InvalidDataException("Invalid tube ID.");}
                        _triangleTubes[lod][s]=ids;
                    }
                }
                if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Trailing tube table data.");
            }
        }
        internal void Fired(int index)
        {
            if(index<0||index>=12)return;
            _loaded&=~(1<<index);
            foreach(GameObject tube in _tubes[index])if(tube!=null)tube.SetActive(false);
        }
        internal Renderer[] Split(M8ModelInstance model,Mesh body,Material[] materials,GameObject parent,int lod)
        {
            var vertices=body.vertices;var normals=body.normals;var uv=body.uv;
            if(lod<0||lod>=3||_triangleTubes[lod].Length!=body.subMeshCount)throw new InvalidDataException("M8 tube table does not match mesh.");
            var buckets=new List<int>[12][];
            var counts=new int[12];
            for(int i=0;i<12;i++){buckets[i]=new List<int>[body.subMeshCount];for(int s=0;s<body.subMeshCount;s++)buckets[i][s]=new List<int>();}
            for(int sub=0;sub<body.subMeshCount;sub++)
            {
                int[] indices=body.GetTriangles(sub);var retained=new List<int>(indices.Length);
                if(indices.Length!=_triangleTubes[lod][sub].Length*3)throw new InvalidDataException("M8 tube triangle table mismatch.");
                for(int i=0;i<indices.Length;i+=3)
                {
                    int tube=_triangleTubes[lod][sub][i/3];
                    var output=tube<0?retained:buckets[tube][sub];
                    output.Add(indices[i]);output.Add(indices[i+1]);output.Add(indices[i+2]);
                    if(tube>=0)counts[tube]++;
                }
                body.SetTriangles(retained.ToArray(),sub);
            }
            var renderers=new Renderer[12];
            for(int tube=0;tube<12;tube++)
            {
                if(counts[tube]<32||counts[tube]>1024)throw new InvalidOperationException("M8 independent tube geometry missing: tube="+tube+" LOD="+lod);
                var lookup=new Dictionary<int,int>();var positions=new List<Vector3>();var ns=new List<Vector3>();var uvs=new List<Vector2>();
                Mesh mesh=new Mesh();model.Own(mesh);mesh.name="M8_Tube_"+tube+"_LOD"+lod;mesh.indexFormat=IndexFormat.UInt16;
                var indices=new int[body.subMeshCount][];
                for(int sub=0;sub<body.subMeshCount;sub++)
                {
                    var input=buckets[tube][sub];var output=new int[input.Count];
                    for(int i=0;i<input.Count;i++)
                    {
                        int id=input[i],mapped;
                        if(!lookup.TryGetValue(id,out mapped)){mapped=positions.Count;lookup.Add(id,mapped);positions.Add(vertices[id]);ns.Add(normals[id]);uvs.Add(uv[id]);}
                        output[i]=mapped;
                    }
                    indices[sub]=output;
                }
                mesh.vertices=positions.ToArray();mesh.normals=ns.ToArray();mesh.uv=uvs.ToArray();mesh.subMeshCount=body.subMeshCount;
                for(int sub=0;sub<body.subMeshCount;sub++)mesh.SetTriangles(indices[sub],sub);
                mesh.RecalculateTangents();mesh.RecalculateBounds();
                var node=new GameObject("M8_Loaded_Tube_"+(tube<6?"L":"R")+(tube%6+1)+"_LOD"+lod);node.layer=parent.layer;
                node.transform.SetParent(parent.transform,false);node.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                _tubes[tube].Add(node);node.SetActive((_loaded&(1<<tube))!=0);renderers[tube]=renderer;
            }
            return renderers;
        }
    }
}
