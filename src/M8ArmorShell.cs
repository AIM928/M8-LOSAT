using System;
using System.IO;
using UnityEngine;

namespace M8LOSATM2
{
    // Offline-selected, exact structural surfaces of this model, not boxes or
    // a convex hull. Private per-process read-only data; meshes are per vehicle.
    internal sealed class M8ArmorShell
    {
        internal static readonly string[] Tokens={
            "Hull Front Alu 5083","Hull rear Allu 5083","Hull Sides Alu 7039","Hull roof Alu 5083","Hull Bottoms Alu 5083",
            "Turret Front Alu 7039","Turret Rear Alu","Turret Sides Alu","Turret Roof Alu","Turret Bottom Alu"
        };
        internal sealed class Plate
        {internal string Token;internal bool Turret;internal Vector3[] Vertices;internal int[] Indices;}
        internal readonly Plate[] Plates=new Plate[10];
        internal readonly string SourceHash;
        private static M8ArmorShell _cached;
        internal static M8ArmorShell Load()
        {
            if(_cached!=null)return _cached;
            using(var stream=typeof(M8ArmorShell).Assembly.GetManifestResourceStream("M8LOSATM2.FittedArmor"))
            {
                if(stream==null)throw new InvalidDataException("M8 fitted armor resource missing.");
                _cached=Read(stream);return _cached;
            }
        }
        private M8ArmorShell(string hash){SourceHash=hash;}
        internal static M8ArmorShell Read(Stream stream)
        {
            if(stream==null||!stream.CanSeek||stream.Length>1000000)throw new InvalidDataException("Invalid fitted armor resource size.");
            using(var r=new BinaryReader(stream))
            {
                if(r.ReadInt32()!=827100||r.ReadInt32()!=1||r.ReadInt32()!=10)throw new InvalidDataException("Wrong fitted armor format.");
                byte[] hash=r.ReadBytes(32);if(hash.Length!=32)throw new EndOfStreamException();
                var shell=new M8ArmorShell(BitConverter.ToString(hash).Replace("-",""));
                int total=0;
                for(int i=0;i<10;i++)
                {
                    string token=r.ReadString();bool turret=r.ReadBoolean();int nv=r.ReadInt32(),ni=r.ReadInt32();
                    if(token!=Tokens[i]||turret!=(i>=5)||nv<3||nv>4000||ni<3||ni>12000||ni%3!=0)
                        throw new InvalidDataException("Invalid fitted armor plate.");
                    var p=new Plate{Token=token,Turret=turret,Vertices=new Vector3[nv],Indices=new int[ni]};
                    for(int j=0;j<nv;j++)
                    {
                        float x=r.ReadSingle(),y=r.ReadSingle(),z=r.ReadSingle();
                        if(!Finite(x)||!Finite(y)||!Finite(z)||Math.Abs(x)>2||y<0||y>2||Math.Abs(z)>3.5f)
                            throw new InvalidDataException("Invalid fitted armor position.");
                        p.Vertices[j]=new Vector3(x,y,z);
                    }
                    for(int j=0;j<ni;j++){int id=r.ReadInt32();if(id<0||id>=nv)throw new InvalidDataException("Invalid fitted armor index.");p.Indices[j]=id;}
                    shell.Plates[i]=p;total+=ni/3;
                }
                if(total!=838||r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Incomplete/trailing fitted armor data.");
                return shell;
            }
        }
        private static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
