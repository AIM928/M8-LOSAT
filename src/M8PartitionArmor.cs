using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Vehicle;
using UnityEngine;

namespace M8LOSATM2
{
    // New armor only for the existing two longitudinal and one transverse
    // infill panels. No original armor/collider/material is edited.
    internal sealed class M8PartitionArmor : IDisposable
    {
        internal const float Ke=5f,Ce=5f;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly FieldInfo Children=typeof(RaycastColliderGroup).GetField("childColliders",Flags);
        private readonly List<Mesh> _meshes=new List<Mesh>();
        private readonly List<MeshCollider> _colliders=new List<MeshCollider>();
        private RaycastColliderGroup _group;private List<Collider> _oldList;private GameObject _root;private bool _disposed;
        internal static M8PartitionArmor Apply(Vehicle vehicle,Transform turret)
        {
            var rig=new M8PartitionArmor();try{rig.Install(vehicle,turret);return rig;}catch{rig.Dispose();throw;}
        }
        private void Install(Vehicle vehicle,Transform turret)
        {
            if(vehicle==null||turret==null||!turret.IsChildOf(vehicle.transform)||Children==null)throw new InvalidOperationException("M8 partition armor owner unavailable.");
            UniformArmor source=null;AarVisual visual=null;
            foreach(var armor in M8ArmorDiscovery.FindOwned(vehicle))
            {
                var aar=armor==null?null:armor.GetComponent<AarVisual>();
                if(armor!=null&&!armor.IsEra&&armor.ArmorType!=null&&aar!=null&&aar.AarMaterial!=null)
                {source=armor;visual=aar;break;}
            }
            if(source==null)throw new InvalidOperationException("Owned native partition armor/X-ray template unavailable.");
            _group=vehicle.GetComponent<RaycastColliderGroup>();
            _oldList=_group==null?null:Children.GetValue(_group) as List<Collider>;
            if(_oldList==null)throw new InvalidOperationException("M8 partition raycast group unavailable.");
            _root=new GameObject("M8_5KE_5CE_Inner_Partitions");_root.SetActive(false);_root.transform.SetParent(turret,false);
            for(int i=0;i<3;i++)
            {
                var node=new GameObject("M8 Inner Partition "+(i==0?"Left":i==1?"Right":"Cross"));node.layer=source.gameObject.layer;
                node.transform.SetParent(_root.transform,false);
                var collision=M8LauncherPartitions.BuildArmorMesh(i,true);_meshes.Add(collision);
                var collider=node.AddComponent<MeshCollider>();collider.sharedMesh=collision;collider.convex=false;collider.isTrigger=false;_colliders.Add(collider);
                var armor=node.AddComponent<UniformArmor>();
                // Copy native interaction flags/material read-only, not template
                // delegates, ownership cache, ERA state or detonation effects.
                foreach(string field in new[]{"_armorType","_normalizesHits","_canShatterLongRods","_canRicochet","_crushThicknessModifier","_surfaceMaterial","_maxSpallAngleKe","_maxSpallAngleCe"})
                {
                    var f=typeof(UniformArmor).GetField(field,Flags);if(f==null)throw new MissingFieldException(field);f.SetValue(armor,f.GetValue(source));
                }
                // Native ThicknessMode 0 means values already expressed in RHA:
                // GetThicknessMultiplier returns 1, independent of source alloy.
                armor.ThicknessListed=(UniformArmor.ThicknessMode)0;armor.PrimarySabotRha=Ke;armor.PrimaryHeatRha=Ce;
                armor.SecondarySabotRha=Ke;armor.SecondaryHeatRha=Ce;armor.AngleMatters=true;armor.SetName(node.name);
                var display=new GameObject("M8 Partition Xray");display.layer=node.layer;display.transform.SetParent(node.transform,false);
                var mesh=M8LauncherPartitions.BuildArmorMesh(i,false);_meshes.Add(mesh);
                display.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=display.AddComponent<MeshRenderer>();renderer.sharedMaterial=visual.AarMaterial;renderer.enabled=false;
                var aar=display.AddComponent<AarVisual>();aar.RenderMode=AarVisual.AarVisualRenderMode.XrayOnly;
                aar.SwitchMaterials=true;aar.PreserveLayerUntilAar=true;aar.HideUntilAar=false;aar.AarMaterial=visual.AarMaterial;aar.SpecificRenderers=new Renderer[]{renderer};aar.Hidden=false;
            }
            // Nested ownership: restore this list before the fitted/interior
            // rigs restore their own earlier snapshots on rollback/unload.
            Children.SetValue(_group,new List<Collider>(_oldList));
            _root.SetActive(true);
            RaycastColliderGroup.AddNewColliderSetToParentGroup(_root.transform);
            var live=Children.GetValue(_group) as List<Collider>;
            foreach(var collider in _colliders)
            {
                if(live==null||!live.Contains(collider))throw new InvalidOperationException("M8 partition collider registration failed.");
                if(collider.GetComponent<UniformArmor>().Unit!=vehicle.BaseUnit)throw new InvalidOperationException("M8 partition armor bound to wrong unit.");
            }
            Physics.SyncTransforms();
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            foreach(var c in _colliders)if(c!=null)c.enabled=false;
            if(_group!=null&&_oldList!=null)Children.SetValue(_group,_oldList);
            if(_root!=null){_root.SetActive(false);UnityEngine.Object.Destroy(_root);}
            foreach(var mesh in _meshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            _meshes.Clear();_colliders.Clear();
        }
    }
}
