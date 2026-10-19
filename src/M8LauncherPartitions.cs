using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M8LOSATM2
{
    // Interior-only infill: two longitudinal walls and a driver/crew cross wall.
    // Infill behind the original inner frames, with its edges seated in them.
    // Front-view LEFT is model +X; front-view RIGHT is model -X. Do not use
    // a camera-dependent left/right convention. Align both banks to their roof
    // end planes, keeping the side walls outside the central optic's envelope.
    // This is not a
    // raised external rail or a change
    // to the fitted armor shell, colliders, crew or twelve launch hardpoints.
    internal static class M8LauncherPartitions
    {
        // Native optic housing outer faces are x=+/- .297373. Recess by
        // 0.2 mm, keep 16 mm thickness, and tuck the front contour into its edge.
        internal const float InnerX=.281173f,OuterX=.297173f;
        internal const float Bottom=.254f,Top=.9885f,Rear=-1.546f,Front=1.529f;
        private sealed class Builder
        {
            internal readonly List<Vector3> P=new List<Vector3>();
            internal readonly List<Vector3> N=new List<Vector3>();
            internal readonly List<Vector2> U=new List<Vector2>();
            internal readonly List<int> T=new List<int>();
            internal void Face(Vector3[] points)
            {
                int start=P.Count;
                Vector3 normal=Vector3.Cross(points[1]-points[0],points[2]-points[0]).normalized;
                foreach(Vector3 p in points)
                {
                    P.Add(p);N.Add(normal);
                    // Interior of the original turret roof's painted UV island.
                    // Reuse the original turret material and all its textures;
                    // no separate flat olive color or private material.
                    // Keep the existing UV scale/origin, including the unchanged
                    // cross wall; expanding the outline must not repaint it.
                    float u=.13f+.11f*Mathf.Clamp01((p.z+1.5302f)/3.0408f);
                    float v=.505f+.031f*Mathf.Clamp01((p.y-.30f)/.68f);
                    if(Math.Abs(normal.z)>.9f)u=.13f+.11f*Mathf.Clamp01((p.x+OuterX)/(2f*OuterX));
                    U.Add(new Vector2(u,v));
                }
                // Ear clipping also handles the small native roof-profile breaks.
                var remaining=new List<int>();for(int i=0;i<points.Length;i++)remaining.Add(i);
                while(remaining.Count>3)
                {
                    bool found=false;
                    for(int i=0;i<remaining.Count;i++)
                    {
                        int a=remaining[(i+remaining.Count-1)%remaining.Count],b=remaining[i],c=remaining[(i+1)%remaining.Count];
                        if(Vector3.Dot(Vector3.Cross(points[b]-points[a],points[c]-points[b]),normal)<=.0000001f)continue;
                        bool occupied=false;
                        foreach(int p in remaining)
                        {
                            if(p==a||p==b||p==c)continue;
                            if(Vector3.Dot(Vector3.Cross(points[b]-points[a],points[p]-points[a]),normal)>=-.0000001f&&
                               Vector3.Dot(Vector3.Cross(points[c]-points[b],points[p]-points[b]),normal)>=-.0000001f&&
                               Vector3.Dot(Vector3.Cross(points[a]-points[c],points[p]-points[c]),normal)>=-.0000001f){occupied=true;break;}
                        }
                        if(occupied)continue;
                        T.Add(start+a);T.Add(start+b);T.Add(start+c);remaining.RemoveAt(i);found=true;break;
                    }
                    if(!found)throw new InvalidOperationException("Invalid M8 partition outline.");
                }
                foreach(int i in remaining)T.Add(start+i);
            }
            internal void Wall(float x0,float x1)
            {
                // Flush with the native optic housing, rather than an outboard
                // second wall. Follow its rear diagonal and upper shoulder so
                // the actual sight aperture and housing face remain uncovered.
                // Seat the lower edge into the original floor (y ~= .259),
                // instead of leaving an 80 mm cut-out exposed at the front.
                // Ends tuck into the native posts, inside shell end planes
                // z=-1.55023/+1.53064. Upper breaks follow the actual roof,
                // including its short front transition, not a rectangular slab.
                Vector2[] profile={new Vector2(Rear,Bottom),
                    new Vector2(1.2729f,Bottom),new Vector2(1.088147f,.821297f),
                    new Vector2(Front,.95624f),
                    new Vector2(1.183f,.9845f),new Vector2(1.039f,Top),
                    new Vector2(-1.191f,Top),new Vector2(Rear,.9698f)};
                var a=new Vector3[profile.Length];var b=new Vector3[profile.Length];
                for(int i=0;i<profile.Length;i++)
                {a[i]=new Vector3(x0,profile[i].y,profile[i].x);b[profile.Length-1-i]=new Vector3(x1,profile[i].y,profile[i].x);}
                Face(a);Face(b);
                for(int i=0;i<profile.Length;i++)
                {
                    int j=(i+1)%profile.Length;
                    Face(new[]{new Vector3(x0,profile[i].y,profile[i].x),new Vector3(x1,profile[i].y,profile[i].x),
                        new Vector3(x1,profile[j].y,profile[j].x),new Vector3(x0,profile[j].y,profile[j].x)});
                }
            }
            internal void CrossWall()
            {
                // Preserve the existing transverse panel and central channel;
                // only the two longitudinal panels need the lower/front infill.
                float x=OuterX,y0=.38f,y1=.98f,z0=.998f,z1=1.014f;
                Vector3 a=new Vector3(-x,y0,z0),b=new Vector3(x,y0,z0),c=new Vector3(x,y1,z0),d=new Vector3(-x,y1,z0);
                Vector3 e=new Vector3(-x,y0,z1),f=new Vector3(x,y0,z1),g=new Vector3(x,y1,z1),h=new Vector3(-x,y1,z1);
                Face(new[]{b,a,d,c});Face(new[]{e,f,g,h});Face(new[]{a,e,h,d});
                Face(new[]{f,b,c,g});Face(new[]{d,h,g,c});Face(new[]{a,b,f,e});
            }
        }
        internal static int FlattenRoofLip(Mesh mesh)
        {
            // Visual mesh only. Collapse the eight welded native lip corners
            // into the wall surface, instead of concealing them with a bevel.
            // Matching all duplicates keeps the existing shell stitched.
            var p=mesh.vertices;var n=mesh.normals;var triangles=mesh.triangles;
            var anchors=new[]{new Vector3(.328548f,.988817f,1.039117f),
                new Vector3(.339262f,.747268f,1.070476f),
                new Vector3(.328548f,.988817f,-1.191148f),
                new Vector3(.339262f,.747268f,-1.159789f)};
            var moved=new bool[p.Length];int count=0;
            for(int i=0;i<p.Length;i++)foreach(var a in anchors)
                if(Math.Abs(Math.Abs(p[i].x)-a.x)<.000002f&&
                   Math.Abs(p[i].y-a.y)<.000002f&&Math.Abs(p[i].z-a.z)<.000002f)
                {moved[i]=true;count++;break;}
            if(count!=23||n.Length!=p.Length)throw new InvalidOperationException("M8 native roof lip geometry differs.");
            for(int i=0;i<p.Length;i++)if(moved[i])
            {
                p[i].x=(p[i].x<0?-1f:1f)*(OuterX-.0002f);
                if(p[i].y>Top-.0002f)p[i].y=Top-.0002f;
            }
            // Recompute only normals incident to the shaved faces; leave all
            // other normals/UVs, indices, materials and submeshes untouched.
            var affected=new bool[p.Length];var sums=new Vector3[p.Length];
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(moved[a]||moved[b]||moved[c])affected[a]=affected[b]=affected[c]=true;
            }
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(!affected[a]&&!affected[b]&&!affected[c])continue;
                var normal=Vector3.Cross(p[b]-p[a],p[c]-p[a]);
                if(affected[a])sums[a]+=normal;if(affected[b])sums[b]+=normal;if(affected[c])sums[c]+=normal;
            }
            for(int i=0;i<p.Length;i++)if(affected[i]&&sums[i].magnitude>.0000001f)n[i]=sums[i].normalized;
            mesh.vertices=p;mesh.normals=n;return count;
        }
        internal static Mesh BuildMesh()
        {
            var b=new Builder();b.Wall(-OuterX,-InnerX);b.Wall(InnerX,OuterX);b.CrossWall();
            var mesh=new Mesh{name="M8_Three_Inner_Partitions",indexFormat=IndexFormat.UInt16};
            mesh.vertices=b.P.ToArray();mesh.normals=b.N.ToArray();mesh.uv=b.U.ToArray();mesh.triangles=b.T.ToArray();
            mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        internal static Mesh BuildArmorMesh(int panel,bool collision)
        {
            if(panel<0||panel>2)throw new ArgumentOutOfRangeException("panel");
            var b=new Builder();
            if(panel==0)b.Wall(-OuterX,-InnerX);else if(panel==1)b.Wall(InnerX,OuterX);else b.CrossWall();
            if(collision)
            {
                // One bidirectional zero-thickness midsurface per plate: the
                // native 5/5 RHA value is charged once, not at both faces of a
                // closed 16 mm display solid. Reuse the exact visual outline.
                int count=panel<2?8:4;var face=new Vector3[count];
                for(int i=0;i<count;i++)
                {
                    var p=b.P[i];if(panel<2)p.x=(panel==0?-1f:1f)*(InnerX+OuterX)*.5f;else p.z=1.006f;
                    face[i]=p;
                }
                b=new Builder();b.Face(face);Array.Reverse(face);b.Face(face);
            }
            var mesh=new Mesh{name="M8_Partition_"+panel+(collision?"_Armor":"_Xray"),indexFormat=IndexFormat.UInt16};
            mesh.vertices=b.P.ToArray();mesh.normals=b.N.ToArray();mesh.uv=b.U.ToArray();mesh.triangles=b.T.ToArray();
            mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        internal static Renderer Create(M8ModelInstance model,Material turretMaterial,GameObject parent,int lod)
        {
            if(model==null||turretMaterial==null||parent==null||lod<0||lod>2)throw new InvalidOperationException("Missing M8 partition owner/material/LOD.");
            Mesh mesh=BuildMesh();model.Own(mesh);
            var node=new GameObject("M8_Three_Inner_Partitions_LOD"+lod);node.layer=parent.layer;node.transform.SetParent(parent.transform,false);
            node.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterial=turretMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;return renderer;
        }
    }
}
