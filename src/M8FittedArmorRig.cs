using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Vehicle;
using UnityEngine;

namespace M8LOSATM2
{
    // Only exterior armor. Never move crew, powertrain, optics or ammunition.
    internal sealed class M8FittedArmorRig : IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private static readonly FieldInfo Children=typeof(RaycastColliderGroup).GetField("childColliders",Flags);
        private static readonly FieldInfo Bounding=typeof(RaycastColliderGroup).GetField("boundingCollider",Flags);
        private static readonly HashSet<Vehicle> Active=new HashSet<Vehicle>();
        private sealed class Saved
        {
            internal UniformArmor Armor;internal MeshCollider Collider;internal MeshFilter Filter;
            internal bool ArmorEnabled,ColliderEnabled;internal Mesh CollisionMesh,VisualMesh;
            internal Transform Parent;internal Vector3 Position,Scale;internal Quaternion Rotation;
            internal bool Moved;
        }
        private readonly List<Saved> _saved=new List<Saved>();
        private readonly List<Mesh> _meshes=new List<Mesh>();
        private Vehicle _vehicle;private RaycastColliderGroup _group;
        private List<Collider> _originalList;private SphereCollider _bound;private float _radius;
        private bool _disposed;

        private static bool Exterior(UniformArmor armor)
        {return armor!=null&&(armor.name.StartsWith("Hull ",StringComparison.Ordinal)||armor.name.StartsWith("Turret ",StringComparison.Ordinal));}
        private static UniformArmor[] Select(Vehicle vehicle)
        {
            var found=new UniformArmor[10];
            foreach(var armor in M8ArmorDiscovery.FindOwned(vehicle))
            {
                if(!Exterior(armor))continue;
                for(int i=0;i<found.Length;i++)if(armor.name.StartsWith(M8ArmorShell.Tokens[i],StringComparison.Ordinal))
                {
                    if(found[i]!=null)throw new InvalidOperationException("Ambiguous owned fitted armor: "+M8ArmorShell.Tokens[i]);
                    found[i]=armor;
                }
            }
            for(int i=0;i<found.Length;i++)
            {
                var a=found[i];var c=a==null?null:a.GetComponent<MeshCollider>();
                if(a==null||c==null||c.convex||a.GetComponent<MeshFilter>()==null||!a.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Fitted armor plate not ready: "+M8ArmorShell.Tokens[i]);
            }
            return found;
        }
        internal static bool IsReady(Vehicle vehicle,out string reason)
        {
            reason="";
            try
            {
                if(vehicle==null||vehicle.BaseUnit==null||Children==null||Bounding==null)throw new InvalidOperationException("Fitted armor native contract unavailable.");
                var group=vehicle.GetComponent<RaycastColliderGroup>();
                var list=group==null?null:Children.GetValue(group) as List<Collider>;
                if(list==null||!(Bounding.GetValue(group) is SphereCollider))throw new InvalidOperationException("Fitted armor raycast group not initialized.");
                foreach(var armor in Select(vehicle))if(!list.Contains(armor.GetComponent<MeshCollider>()))throw new InvalidOperationException("Native armor absent from owned raycast group.");
                M8ArmorShell.Load();return true;
            }
            catch(Exception e){reason=e.Message;return false;}
        }
        internal static M8FittedArmorRig Apply(Vehicle vehicle,Transform turret)
        {
            string reason;if(!IsReady(vehicle,out reason)||turret==null||!turret.IsChildOf(vehicle.transform))throw new InvalidOperationException(reason.Length==0?"Fitted armor turret frame unavailable.":reason);
            if(!Active.Add(vehicle))throw new InvalidOperationException("Fitted armor already applied to this vehicle.");
            var rig=new M8FittedArmorRig{_vehicle=vehicle};
            try{rig.Install(turret);return rig;}catch{rig.Dispose();throw;}
        }
        private void Install(Transform turret)
        {
            var data=M8ArmorShell.Load();var plates=Select(_vehicle);
            _group=_vehicle.GetComponent<RaycastColliderGroup>();_originalList=(List<Collider>)Children.GetValue(_group);
            _bound=(SphereCollider)Bounding.GetValue(_group);_radius=_bound.radius;
            var live=new List<Collider>(_originalList);
            // Remove obsolete EXTERIOR entries, not just collider.enabled: native
            // SetState re-enables every entry still in this raycast group list.
            foreach(var armor in M8ArmorDiscovery.FindOwned(_vehicle))
            {
                if(!Exterior(armor))continue;
                var collider=armor.GetComponent<MeshCollider>();
                if(collider==null)throw new InvalidOperationException("Owned exterior plate has no mesh collider: "+armor.name);
                var filter=armor.GetComponent<MeshFilter>();
                var t=armor.transform;
                _saved.Add(new Saved{Armor=armor,Collider=collider,Filter=filter,ArmorEnabled=armor.enabled,ColliderEnabled=collider.enabled,
                    CollisionMesh=collider.sharedMesh,VisualMesh=filter==null?null:filter.sharedMesh,Parent=t.parent,Position=t.localPosition,Rotation=t.localRotation,Scale=t.localScale});
                if(Array.IndexOf(plates,armor)<0){armor.enabled=false;collider.enabled=false;while(live.Remove(collider)){};}
            }
            Children.SetValue(_group,live);
            for(int i=0;i<plates.Length;i++)
            {
                var armor=plates[i];var plate=data.Plates[i];
                var saved=_saved.Find(s=>ReferenceEquals(s.Armor,armor));
                saved.Moved=true;
                var frame=plate.Turret?turret:_vehicle.transform;
                var t=armor.transform;t.SetParent(frame,false);t.localPosition=Vector3.zero;t.localRotation=Quaternion.identity;t.localScale=Vector3.one;
                var mesh=new Mesh{name="M8_Fitted_"+(plate.Turret?"Turret_":"Hull_")+i};_meshes.Add(mesh);
                mesh.vertices=plate.Vertices;mesh.triangles=plate.Indices;mesh.RecalculateNormals();mesh.RecalculateBounds();
                saved.Collider.sharedMesh=null;saved.Collider.sharedMesh=mesh;
                saved.Filter.sharedMesh=mesh;armor.enabled=true;
                // Keep the native broad/narrow phase enabled state and layer.
                // Mesh-local positions are the visual's exact frame positions.
            }
            ExpandBound(data,turret);
            Physics.SyncTransforms();
        }
        private void ExpandBound(M8ArmorShell data,Transform turret)
        {
            // Conservative all-yaw envelope. The Frobenius scale bound also
            // covers nonuniform/sheared parent transforms; never shrink native.
            var hull=_vehicle.transform;Vector3 origin=_bound.transform.InverseTransformPoint(hull.position);
            float sx=(_bound.transform.InverseTransformPoint(hull.TransformPoint(Vector3.right))-origin).magnitude;
            float sy=(_bound.transform.InverseTransformPoint(hull.TransformPoint(Vector3.up))-origin).magnitude;
            float sz=(_bound.transform.InverseTransformPoint(hull.TransformPoint(Vector3.forward))-origin).magnitude;
            float scale=(float)Math.Sqrt(sx*sx+sy*sy+sz*sz),reach=0f;
            foreach(var plate in data.Plates)foreach(var p in plate.Vertices)
            {
                // Triangle vertices are turret-local or hull-local; a sphere
                // around the turret pivot bounds every future yaw, not only rest.
                float radius=plate.Turret?p.magnitude+hull.InverseTransformPoint(turret.position).magnitude:p.magnitude;
                reach=Math.Max(reach,radius);
            }
            _bound.radius=Math.Max(_radius,(origin-_bound.center).magnitude+reach*scale+.05f);
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(_group!=null&&_originalList!=null)Children.SetValue(_group,_originalList);
            foreach(var saved in _saved)
            {
                if(saved.Armor==null)continue;
                var t=saved.Armor.transform;
                if(saved.Moved){t.SetParent(saved.Parent,false);t.localPosition=saved.Position;t.localRotation=saved.Rotation;t.localScale=saved.Scale;}
                if(saved.Collider!=null){saved.Collider.sharedMesh=null;saved.Collider.sharedMesh=saved.CollisionMesh;saved.Collider.enabled=saved.ColliderEnabled;}
                if(saved.Filter!=null)saved.Filter.sharedMesh=saved.VisualMesh;
                saved.Armor.enabled=saved.ArmorEnabled;
            }
            if(_bound!=null)_bound.radius=_radius;
            foreach(var mesh in _meshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            _meshes.Clear();_saved.Clear();if(!ReferenceEquals(_vehicle,null))Active.Remove(_vehicle);
        }
    }
}
