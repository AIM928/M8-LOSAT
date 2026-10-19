using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace M8LOSATM2
{
    internal sealed class M8TrackRig
    {
        private sealed class Loop
        {
            internal Vector3[] Points;
            internal float[] Starts;
            internal float Length;
            internal void Evaluate(float distance, out Vector3 position, out Quaternion rotation)
            {
                float phase=Mathf.Repeat(distance,Length);int segment=0;
                while(segment+1<Starts.Length && Starts[segment+1]<=phase)segment++;
                int next=(segment+1)%Points.Length;
                float length=(next==0?Length:Starts[next])-Starts[segment];
                float t=(phase-Starts[segment])/length;
                position=Vector3.Lerp(Points[segment],Points[next],t);
                Vector3 first=Points[next]-Points[(segment+Points.Length-1)%Points.Length];
                Vector3 second=Points[(next+1)%Points.Length]-Points[segment];
                float a=-Mathf.Atan2(first.y,first.z)*Mathf.Rad2Deg;
                float b=-Mathf.Atan2(second.y,second.z)*Mathf.Rad2Deg;
                rotation=Quaternion.AngleAxis(Mathf.LerpAngle(a,b,t),Vector3.right);
            }
        }
        private readonly Transform[] _bones;
        private readonly Matrix4x4[] _bindposes;
        private readonly int[] _kind,_side;
        private readonly float[] _phase,_radius;
        private readonly Vector3[] _restPosition;
        private readonly Loop[] _loops=new Loop[2];
        private readonly int[][] _vertexBones=new int[3][];
        private readonly float[] _travel=new float[2];
        private readonly float[] _wheelAngles=new float[133];

        internal M8TrackRig(M8ModelInstance model,string assetDirectory)
        {
            string file=Path.Combine(assetDirectory,"GHPC","M8Tracks.skinbin");
            if(!File.Exists(file) || new FileInfo(file).Length>2000000)throw new InvalidDataException("Missing or oversized M8 track rig.");
            Transform parent=model.Find("M8_Tracks_Static_Visual");
            if(parent==null)throw new InvalidDataException("M8 track mesh node is missing.");
            using(var reader=new BinaryReader(File.OpenRead(file)))
            {
                if(reader.ReadInt32()!=811120 || reader.ReadInt32()!=1)throw new InvalidDataException("Wrong track rig version.");
                int count=reader.ReadInt32();if(count!=133)throw new InvalidDataException("Unexpected track bone budget.");
                _bones=new Transform[count];_bindposes=new Matrix4x4[count];_kind=new int[count];_side=new int[count];
                _phase=new float[count];_radius=new float[count];_restPosition=new Vector3[count];
                for(int i=0;i<count;i++)
                {
                    _kind[i]=reader.ReadInt32();_side[i]=reader.ReadInt32();
                    if(_kind[i]<0||_kind[i]>2||_side[i]<0||_side[i]>1)throw new InvalidDataException("Invalid track bone.");
                    _restPosition[i]=ReadVector(reader);_phase[i]=ReadFloat(reader);_radius[i]=ReadFloat(reader);
                    _bones[i]=new GameObject("M8_Track_Bone_"+i).transform;
                    _bones[i].gameObject.layer=parent.gameObject.layer;_bones[i].SetParent(parent,false);
                }
                if(reader.ReadInt32()!=2)throw new InvalidDataException("M8 requires independent left/right loops.");
                for(int side=0;side<2;side++)
                {
                    int n=reader.ReadInt32();if(n!=64)throw new InvalidDataException("Unexpected track shoe count.");
                    var loop=new Loop{Points=new Vector3[n],Starts=new float[n]};
                    for(int i=0;i<n;i++)loop.Points[i]=ReadVector(reader);
                    for(int i=0;i<n;i++)
                    {
                        loop.Starts[i]=loop.Length;
                        float distance=Vector3.Distance(loop.Points[i],loop.Points[(i+1)%n]);
                        if(distance<.1f||distance>.3f)throw new InvalidDataException("Discontinuous track loop.");
                        loop.Length+=distance;
                    }
                    _loops[side]=loop;
                }
                if(reader.ReadInt32()!=3)throw new InvalidDataException("Track rig LOD mismatch.");
                for(int lod=0;lod<3;lod++)
                {
                    int n=reader.ReadInt32();if(n<1000||n>150000)throw new InvalidDataException("Track vertex budget exceeded.");
                    int[] ids=new int[n];
                    for(int i=0;i<n;i++){ids[i]=reader.ReadInt32();if(ids[i]<0||ids[i]>=count)throw new InvalidDataException("Invalid track weight.");}
                    _vertexBones[lod]=ids;
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("Trailing track rig data.");
            }
            UpdateBones();
            for(int i=0;i<_bones.Length;i++)_bindposes[i]=Matrix4x4.TRS(_bones[i].localPosition,_bones[i].localRotation,Vector3.one).inverse;
        }
        private static float ReadFloat(BinaryReader reader)
        {float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Invalid track value.");return value;}
        private static Vector3 ReadVector(BinaryReader reader){return new Vector3(ReadFloat(reader),ReadFloat(reader),ReadFloat(reader));}
        internal Renderer MakeRenderer(GameObject node,Mesh mesh,Material[] materials,int lod)
        {
            if(lod<0||lod>=3||mesh.vertexCount!=_vertexBones[lod].Length)throw new InvalidDataException("Track rig does not match current mesh.");
            var weights=new BoneWeight[mesh.vertexCount];
            for(int i=0;i<weights.Length;i++){weights[i].boneIndex0=_vertexBones[lod][i];weights[i].weight0=1f;}
            mesh.boneWeights=weights;mesh.bindposes=_bindposes;
            var renderer=node.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh=mesh;renderer.sharedMaterials=materials;renderer.bones=_bones;
            renderer.rootBone=_bones[0].parent;renderer.quality=SkinQuality.Bone1;
            Bounds bounds=mesh.bounds;bounds.Expand(.12f);renderer.localBounds=bounds;
            renderer.updateWhenOffscreen=false;
            return renderer;
        }
        internal void Advance(float leftMetres,float rightMetres)
        {
            if(Time.deltaTime<=0f)return;
            float left=ValidStep(leftMetres)?leftMetres:0f,right=ValidStep(rightMetres)?rightMetres:0f;
            _travel[0]=Mathf.Repeat(_travel[0]-left,_loops[0].Length);
            _travel[1]=Mathf.Repeat(_travel[1]-right,_loops[1].Length);
            for(int i=0;i<_bones.Length;i++)if(_kind[i]==2)
                _wheelAngles[i]=Mathf.Repeat(_wheelAngles[i]+(_side[i]==0?left:right)/_radius[i]*Mathf.Rad2Deg,360f);
            UpdateBones();
        }
        private static bool ValidStep(float step){return !float.IsNaN(step)&&!float.IsInfinity(step)&&Mathf.Abs(step)<2f;}
        private void UpdateBones()
        {
            for(int i=0;i<_bones.Length;i++)
            {
                Vector3 position=_restPosition[i];Quaternion rotation=Quaternion.identity;
                if(_kind[i]==1)_loops[_side[i]].Evaluate(_phase[i]+_travel[_side[i]],out position,out rotation);
                else if(_kind[i]==2)rotation=Quaternion.AngleAxis(_wheelAngles[i],Vector3.right);
                _bones[i].localPosition=position;_bones[i].localRotation=rotation;
            }
        }
    }
}
