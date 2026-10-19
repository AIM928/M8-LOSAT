using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC.Effects;
using UnityEngine;

namespace M8LOSATM2
{
    // External ammunition is not the Bradley's troop/crew compartment. Keep
    // native propellant fire simulation, without changing any crew evacuation rule.
    internal sealed class M8ExternalAmmoFire : IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private readonly FlammablesManager _manager;
        private readonly Compartment[] _original;
        private Compartment[] _installed;
        private readonly Compartment[] _banks=new Compartment[2];
        private readonly GameObject[] _nodes=new GameObject[2];
        private bool _disposed;

        private static object Field(object o,string name)
        {var f=o.GetType().GetField(name,Flags);if(f==null)throw new MissingFieldException(o.GetType().FullName,name);return f.GetValue(o);}

        internal M8ExternalAmmoFire(Transform inactiveParent,FlammablesCluster template)
        {
            var source=Field(template,"_compartment") as Compartment;
            if(source==null)throw new InvalidOperationException("Native missile compartment unavailable.");
            _manager=Field(source,"_manager") as FlammablesManager;
            if(_manager==null||_manager.Compartments==null||Array.IndexOf(_manager.Compartments,source)<0)
                throw new InvalidOperationException("Native missile fire manager not ready.");
            _original=_manager.Compartments;
            try
            {
                for(int bank=0;bank<2;bank++)
                {
                    var node=new GameObject("M8_External_Missile_Fire_"+(bank==0?"L":"R"));_nodes[bank]=node;
                    node.SetActive(false);node.transform.SetParent(inactiveParent,false);
                    var c=node.AddComponent<Compartment>();_banks[bank]=c;
                    c.Name="M8 external missile rack "+(bank==0?"left":"right");
                    c.Parent=null;c.IsCrewCompartment=false;c.UnsecuredFiresHere=false;
                    // References only: reuse the actual six missile hit colliders.
                    // No new collider, proxy volume, armor, or crew fire subscriber.
                    c.Volumes=new Collider[6];c.TotalVolume=1f;
                    c.InternalTempBoost=source.InternalTempBoost;c.MaxTempC=source.MaxTempC;
                    c.Detachables=new List<DetachableParent>();
                    var marker=new GameObject("M8_External_Fire_Marker");marker.transform.SetParent(node.transform,false);
                    Vector3 p=Vector3.zero;
                    for(int i=bank*6;i<bank*6+6;i++)p+=M8Hardpoints.Positions[i];
                    marker.transform.localPosition=new Vector3(p.x/6,p.y/6,0);
                    c.Exits=new List<CompartmentExit>{new CompartmentExit{
                        Name="open external missile rack",PositionMarker=marker.transform,
                        SourceType=FlammableSourceType.Surface,Radius=.2f,FlameHeightThreshold=.1f,Closed=false}};
                    var cluster=new FlammablesCluster{Name=c.Name,TempGrowCoeff=template.TempGrowCoeff,
                        FireProximityFactor=template.FireProximityFactor,ExplosionProximityFactor=template.ExplosionProximityFactor,
                        MaxTempC=template.MaxTempC};
                    var fireMarker=typeof(FlammablesCluster).GetField("_fireMarker",Flags);
                    if(fireMarker==null)throw new MissingFieldException("FlammablesCluster._fireMarker");
                    fireMarker.SetValue(cluster,marker.transform);
                    c.Clusters=new List<FlammablesCluster>{cluster};
                    c.RegisterFlammablesManager(_manager);
                }
            }
            catch{Dispose();throw;}
        }
        internal void Bind(int index,FlammableItem item,Collider collider)
        {
            if(_disposed||index<0||index>=12||item==null||collider==null)throw new InvalidOperationException("Invalid external missile binding.");
            var c=_banks[index/6];var cluster=c.Clusters[0];
            if(c.Volumes[index%6]!=null)throw new InvalidOperationException("Duplicate external missile binding.");
            item.RegisterCluster(cluster);cluster.Items.Add(item);c.Volumes[index%6]=collider;
        }
        internal void Commit()
        {
            if(_disposed||_installed!=null||!ReferenceEquals(_manager.Compartments,_original))throw new InvalidOperationException("External missile fire registration changed.");
            foreach(var c in _banks)
            {
                if(c.Clusters[0].Items.Count!=6)throw new InvalidOperationException("External rack must contain six missiles.");
                foreach(var volume in c.Volumes)if(volume==null)throw new InvalidOperationException("Incomplete external rack volume references.");
            }
            _installed=new Compartment[_original.Length+2];Array.Copy(_original,_installed,_original.Length);
            Array.Copy(_banks,0,_installed,_original.Length,2);_manager.Compartments=_installed;
            // Parent remains inactive until M8InteriorRig finishes the entire rig.
            // Native Awake then registers clusters/items and initializes open exits.
            foreach(var node in _nodes)node.SetActive(true);
        }
        internal void ValidateInitialized()
        {
            // Unity logs Awake exceptions rather than reliably propagating them
            // to SetActive. Reject an incompletely initialized native fire rig.
            foreach(var c in _banks)
            {
                if(!ReferenceEquals(Field(c.Clusters[0],"_compartment"),c)||
                    !ReferenceEquals(Field(c.Exits[0],"_compartment"),c))
                    throw new InvalidOperationException("External missile fire Awake did not complete.");
            }
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(_manager!=null&&_installed!=null)
            {
                if(ReferenceEquals(_manager.Compartments,_installed))_manager.Compartments=_original;
                else if(_manager.Compartments!=null)
                {
                    // Preserve any later third-party entries; remove only our two.
                    var retained=new List<Compartment>();
                    foreach(var c in _manager.Compartments)
                        if(!ReferenceEquals(c,_banks[0])&&!ReferenceEquals(c,_banks[1]))retained.Add(c);
                    _manager.Compartments=retained.ToArray();
                }
            }
            foreach(var node in _nodes)if(node!=null){node.SetActive(false);UnityEngine.Object.Destroy(node);}
        }
    }
}
