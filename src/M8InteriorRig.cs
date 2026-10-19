using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Crew;
using GHPC.Effects;
using GHPC.Equipment;
using GHPC.PhysicsHelpers;
using GHPC.Vehicle;
using UnityEngine;
using NativeDamage = GHPC.Equipment.DestructibleComponent;

namespace M8LOSATM2
{
    // Per-instance reversible transplant. Do not enable the obsolete box-armor rig.
    internal sealed class M8InteriorRig : IDisposable
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        // User-selected per-missile durability; not an armor thickness or HE yield.
        private const float MissileHealth=10f,MissileDamageThreshold=.5f;
        private sealed class SavedTransform
        {
            internal Transform Node,Parent; internal Vector3 Position,Scale; internal Quaternion Rotation;
            internal SavedTransform(Transform t){Node=t;Parent=t.parent;Position=t.localPosition;Scale=t.localScale;Rotation=t.localRotation;}
            internal void Restore(){if(Node==null)return;Node.SetParent(Parent,false);Node.localPosition=Position;Node.localRotation=Rotation;Node.localScale=Scale;}
        }
        private sealed class SavedMesh
        {internal MeshFilter Filter;internal MeshCollider Collider;internal Mesh Visual,Collision;}
        private sealed class SavedCrew
        {internal CrewManager.CrewMember Member;internal GameObject Anchor;}
        private readonly List<SavedTransform> _transforms=new List<SavedTransform>();
        private readonly List<SavedMesh> _meshStates=new List<SavedMesh>();
        private readonly Dictionary<GameObject,bool> _disabled=new Dictionary<GameObject,bool>();
        private readonly Dictionary<AarVisual,bool> _hiddenVisuals=new Dictionary<AarVisual,bool>();
        private readonly Dictionary<FlammablesCluster,List<FlammableItem>> _clusters=new Dictionary<FlammablesCluster,List<FlammableItem>>();
        private readonly List<Mesh> _meshes=new List<Mesh>();
        private Material _missileMaterial;
        private M8ExternalAmmoFire _externalFire;
        private readonly List<SavedCrew> _crew=new List<SavedCrew>();
        private readonly GameObject[] _rounds=new GameObject[12];
        private readonly FlammableItem[] _fuel=new FlammableItem[12];
        internal readonly NativeDamage[] MissileDamage=new NativeDamage[12];
        private readonly List<Action<IDestructible>> _handlers=new List<Action<IDestructible>>();
        private readonly List<Action<FlammableItem>> _burnHandlers=new List<Action<FlammableItem>>();
        private readonly List<GameObject> _owned=new List<GameObject>();
        private RaycastColliderGroup _group;private object _oldGroupColliders;
        private bool _disposed;private Action<int> _lost;

        private static object Field(object owner,string name)
        {var f=owner.GetType().GetField(name,Flags);if(f==null)throw new MissingFieldException(owner.GetType().FullName,name);return f.GetValue(owner);}
        private static void Set(object owner,string name,object value)
        {var f=owner.GetType().GetField(name,Flags);if(f==null)throw new MissingFieldException(owner.GetType().FullName,name);f.SetValue(owner,value);}

        private static Dictionary<string,GameObject> FindParts(Vehicle vehicle)
        {
            var parts=new Dictionary<string,GameObject>(StringComparer.Ordinal);
            foreach(var aar in Resources.FindObjectsOfTypeAll<AarVisual>())
            {
                if(aar==null||!aar.gameObject.scene.IsValid()||!M8InteriorLayout.IsInterior(aar.name))continue;
                // Nested native follow rigs can detach the gun's mantlet twice.
                // CodeUtils follows only ONE OriginalParent hop. Armor retains
                // the actual owner cached before detachment, including VariableArmor.
                var variable=aar.GetComponent<VariableArmor>();var uniform=aar.GetComponent<UniformArmor>();
                bool owned=(variable!=null&&ReferenceEquals(variable.Unit,vehicle.BaseUnit))||
                    (uniform!=null&&ReferenceEquals(uniform.Unit,vehicle.BaseUnit))||
                    ReferenceEquals(GHPC.Utility.CodeUtils.FindComponentInParentFollowAware<IUnit>(aar.transform),vehicle.BaseUnit);
                if(!owned)continue;
                if(parts.ContainsKey(aar.name))throw new InvalidOperationException("Ambiguous interior component: "+aar.name);
                parts.Add(aar.name,aar.gameObject);
            }
            return parts;
        }
        internal static bool IsReady(Vehicle vehicle,out string reason)
        {
            reason="";
            try
            {
                var parts=FindParts(vehicle);
                foreach(string name in M8InteriorLayout.Parts)if(!parts.ContainsKey(name)){reason="M2 interior not ready: "+name;return false;}
                GameObject round;if(!parts.TryGetValue("Ammo_TowTube_001",out round)||round.GetComponent<NativeDamage>()==null||round.GetComponent<FlammableItem>()==null)
                {reason="Native missile damage template unavailable.";return false;}
                var fireCluster=Field(round.GetComponent<FlammableItem>(),"_cluster") as FlammablesCluster;
                if(fireCluster==null)
                {reason="Native missile fire cluster not ready.";return false;}
                var fireCompartment=Field(fireCluster,"_compartment") as Compartment;
                var fireManager=fireCompartment==null?null:Field(fireCompartment,"_manager") as FlammablesManager;
                if(fireManager==null||fireManager.Compartments==null||Array.IndexOf(fireManager.Compartments,fireCompartment)<0)
                {reason="Native missile fire manager not ready.";return false;}
                var launcher=M8WeaponBinding.FindLauncher(vehicle);
                if(launcher==null||launcher.BreechHitZone==null||(float)Field(launcher.BreechHitZone,"_fullHealth")<=0)
                {reason="Native M2 launcher durability not ready.";return false;}
                var visual=round.GetComponent<AarVisual>();
                if(visual==null||visual.AarMaterial==null||!visual.AarMaterial.HasProperty("_Color"))
                {reason="Native missile X-ray material unavailable.";return false;}
                var manager=vehicle.BaseUnit.CrewManager;
                if(manager==null||manager.CrewMembers==null){reason="Native crew manager unavailable.";return false;}
                foreach(CrewPosition pos in new[]{CrewPosition.Driver,CrewPosition.Gunner,CrewPosition.Commander})
                {
                    var member=manager.GetCrewMember(pos);
                    if(member==null||(member.AutoBoneRig==null&&member.BodyPartRegistry==null)||Head(member)==null)
                    {reason="Waiting for crew skeleton: "+pos;return false;}
                }
                return true;
            }
            catch(Exception e){reason=e.Message;return false;}
        }
        internal static M8InteriorRig Apply(Vehicle vehicle,Transform turret,Action<int> lost)
        {
            var rig=new M8InteriorRig{_lost=lost};
            try{rig.Install(vehicle,turret);return rig;}catch{rig.Dispose();throw;}
        }
        private void Install(Vehicle vehicle,Transform turret)
        {
            var parts=FindParts(vehicle);
            // Take a private list before changing cluster membership; retain all native
            // event subscribers and drivetrain/FCS references to the original components.
            foreach(var part in parts.Values)
            {
                var item=part.GetComponent<FlammableItem>();if(item==null)continue;
                var cluster=Field(item,"_cluster") as FlammablesCluster;
                if(cluster!=null&&!_clusters.ContainsKey(cluster))
                {_clusters.Add(cluster,cluster.Items);cluster.Items=new List<FlammableItem>(cluster.Items);}
            }
            var template=parts["Ammo_TowTube_001"];
            var templateItem=template.GetComponent<FlammableItem>();
            var ammoCluster=Field(templateItem,"_cluster") as FlammablesCluster;
            if(ammoCluster==null)throw new InvalidOperationException("Native missile flammables cluster not initialized.");
            var launcher=M8WeaponBinding.FindLauncher(vehicle);
            if(launcher==null||launcher.BreechHitZone==null)throw new InvalidOperationException("Native M2 launcher durability unavailable.");
            var durability=launcher.BreechHitZone;
            float fullHealth=MissileHealth;
            if(float.IsNaN(fullHealth)||float.IsInfinity(fullHealth)||fullHealth<=0||
                float.IsNaN(durability.DamageThreshold)||float.IsInfinity(durability.DamageThreshold)||durability.DamageThreshold<0||
                float.IsNaN(durability.PressureTolerance)||float.IsInfinity(durability.PressureTolerance)||durability.PressureTolerance<=0)
                throw new InvalidOperationException("Invalid native launcher durability.");
            var sourceMaterial=template.GetComponent<AarVisual>().AarMaterial;
            if(sourceMaterial==null||!sourceMaterial.HasProperty("_Color"))throw new InvalidOperationException("Native missile X-ray material unavailable.");
            _missileMaterial=new Material(sourceMaterial){name="M8_MGM166_Red_Xray"};
            _missileMaterial.SetColor("_Color",new Color(.55f,.012f,.018f,1));
            if(_missileMaterial.HasProperty("_EmissionColor"))_missileMaterial.SetColor("_EmissionColor",new Color(.08f,.001f,.002f,1));
            if(_missileMaterial.HasProperty("_OutlineColor"))_missileMaterial.SetColor("_OutlineColor",new Color(.28f,.005f,.007f,1));
            // Remove the obsolete box at the original M2 launcher position. Its
            // health is only a template; twelve independent live hit zones replace it.
            DisableObsolete(durability.gameObject);

            // Build while inactive so Awake sees the final mesh, owner and health data.
            var staging=new GameObject("M8_LOSAT_Missile_Internals");_owned.Add(staging);
            staging.SetActive(false);staging.transform.SetParent(turret,false);
            _externalFire=new M8ExternalAmmoFire(staging.transform,ammoCluster);
            for(int i=0;i<12;i++)
            {
                var go=UnityEngine.Object.Instantiate(template,staging.transform,false);
                go.name="M8_LOSAT_MGM166_"+(i<6?"L":"R")+(i%6+1);
                go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
                go.layer=GHPC.Utility.CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
                var mesh=MissileMesh(i);_meshes.Add(mesh);
                // AAR silhouette only: keep the prior physical hit mesh so a
                // display silhouette does not alter missile vulnerability.
                var display=MissileDisplayMesh(mesh);_meshes.Add(display);
                go.GetComponent<MeshFilter>().sharedMesh=display;
                var collider=go.GetComponent<MeshCollider>();
                if(collider==null)throw new InvalidOperationException("Native missile template has no mesh collider.");
                collider.sharedMesh=null;collider.sharedMesh=mesh;
                var dc=go.GetComponent<NativeDamage>();dc.SetName("MGM-166 "+(i<6?"L":"R")+(i%6+1));
                Set(dc,"_health",fullHealth);Set(dc,"_fullHealth",fullHealth);
                Set(dc,"_damageThreshold",MissileDamageThreshold);Set(dc,"_pressureTolerance",durability.PressureTolerance);dc.Hidden=false;
                // Never retain a prefab proxy reference targeting another missile.
                Set(dc,"_proxyHitZone",null);Set(dc,"_parent",null);Set(dc,"_children",new List<NativeDamage>());
                var renderer=go.GetComponent<Renderer>();renderer.forceRenderingOff=false;renderer.enabled=false;
                var aar=go.GetComponent<AarVisual>();aar.Hidden=false;aar.AarMaterial=_missileMaterial;
                aar.SpecificRenderers=new[]{renderer};
                // Keep the native X-ray/AAR visibility gate. The source renderer
                // was force-hidden by the exterior swap; clones must not inherit it.
                var armor=go.GetComponent<UniformArmor>();if(armor!=null)armor.SetName(dc.Name);
                var item=go.GetComponent<FlammableItem>();
                // Kinetic missile has no HE warhead. Preserve native TOW propellant
                // burn behavior (_explosive=false), not an invented HE explosion.
                Set(item,"_explosive",false);_externalFire.Bind(i,item,collider);
                _rounds[i]=go;MissileDamage[i]=dc;_fuel[i]=item;
                int index=i;Action<IDestructible> handler=dead=>{if(!_disposed&&_lost!=null)_lost(index);};
                dc.Destroyed+=handler;_handlers.Add(handler);
                Action<FlammableItem> burn=burning=>{if(!_disposed&&dc!=null&&!dc.IsDestroyed)dc.SetHealthPercent(0);};
                item.Ignited+=burn;_burnHandlers.Add(burn);
            }
            for(int i=0;i<M8InteriorLayout.Parts.Length;i++)
            {
                GameObject part=parts[M8InteriorLayout.Parts[i]];
                // Positions in the table are zero-yaw hull coordinates. Central crew
                // equipment follows the launcher, propulsion remains hull-fixed.
                Transform frame=M8InteriorLayout.TurretPart(i)?turret:vehicle.transform;
                Vector3 center=M8InteriorLayout.Centers[i];
                if(M8InteriorLayout.TurretPart(i))center-=turret.localPosition;
                FitPart(part,frame,center,M8InteriorLayout.Sizes[i]);
            }
            foreach(var pair in parts)
            {
                if(Array.IndexOf(M8InteriorLayout.Parts,pair.Key)>=0)continue;
                DisableObsolete(pair.Value);
                var item=pair.Value.GetComponent<FlammableItem>();if(item!=null)
                    foreach(var cluster in _clusters.Keys)cluster.Items.Remove(item);
            }
            // The old cannon sleeve is detached with the native raycast armor rig;
            // ownership is required, not hierarchy or a global name-only search.
            foreach(var armor in M8ArmorDiscovery.FindOwned(vehicle))
                if(armor.name.StartsWith("Turret Cannon Sleeve ",StringComparison.Ordinal))DisableObsolete(armor.gameObject);
            var manager=vehicle.BaseUnit.CrewManager;
            MoveCrew(vehicle,manager.GetCrewMember(CrewPosition.Driver),vehicle.transform,0);
            MoveCrew(vehicle,manager.GetCrewMember(CrewPosition.Gunner),turret,1);
            MoveCrew(vehicle,manager.GetCrewMember(CrewPosition.Commander),turret,2);
            _group=vehicle.GetComponent<RaycastColliderGroup>();
            if(_group==null)throw new InvalidOperationException("Native vehicle raycast group unavailable.");
            _oldGroupColliders=Field(_group,"childColliders");
            Set(_group,"childColliders",new List<Collider>((List<Collider>)_oldGroupColliders));
            _externalFire.Commit();
            staging.SetActive(true);
            _externalFire.ValidateInitialized();
            RaycastColliderGroup.AddNewColliderSetToParentGroup(staging.transform);
            foreach(var cluster in _clusters.Keys){cluster.RefreshExplosivesStatus();cluster.RefreshUndetonatedExplosivesStatus();}
            Physics.SyncTransforms();
        }

        private void FitPart(GameObject go,Transform frame,Vector3 center,Vector3 size)
        {
            var filter=go.GetComponent<MeshFilter>();var collider=go.GetComponent<MeshCollider>();
            if(filter==null||filter.sharedMesh==null||collider==null)throw new InvalidOperationException("Interior mesh/collider missing: "+go.name);
            var original=filter.sharedMesh;
            var points=original.vertices;
            Matrix4x4 source=frame.worldToLocalMatrix*go.transform.localToWorldMatrix;
            var bounds=new Bounds(source.MultiplyPoint3x4(points[0]),Vector3.zero);
            for(int i=0;i<points.Length;i++){points[i]=source.MultiplyPoint3x4(points[i]);bounds.Encapsulate(points[i]);}
            Vector3 s=bounds.size;
            if(s.x<.00001f||s.y<.00001f||s.z<.00001f)throw new InvalidOperationException("Degenerate internal mesh: "+go.name);
            for(int i=0;i<points.Length;i++)points[i]=Vector3.Scale(points[i]-bounds.center,new Vector3(size.x/s.x,size.y/s.y,size.z/s.z))+center;
            var mesh=UnityEngine.Object.Instantiate(original);_meshes.Add(mesh);mesh.name="M8_Internal_"+go.name;mesh.vertices=points;mesh.RecalculateNormals();mesh.RecalculateBounds();
            _transforms.Add(new SavedTransform(go.transform));
            _meshStates.Add(new SavedMesh{Filter=filter,Collider=collider,Visual=original,Collision=collider.sharedMesh});
            go.transform.SetParent(frame,false);go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
            filter.sharedMesh=mesh;collider.sharedMesh=null;collider.sharedMesh=mesh;
        }
        private static Transform Head(CrewManager.CrewMember member)
        {
            if(member==null)return null;
            // Stock M2 has a pre-rigged static skeleton/registry and no AutoBoneRig
            // or animation triggers. Do not wait forever for a Humvee-only component.
            IEnumerable<Transform> bones;
            if(member.AutoBoneRig!=null)
            {if(member.AutoBoneRig.AllCharacterBones==null)return null;bones=member.AutoBoneRig.AllCharacterBones;}
            else if(member.BodyPartRegistry!=null)bones=member.BodyPartRegistry.GetComponentsInChildren<Transform>(true);
            else return null;
            foreach(var t in bones)
            {if(t==null)continue;var n=t.name.ToLowerInvariant();if(n=="head"||n=="catrighead"||n.EndsWith(":head"))return t;}
            // Stock M2 enumerates Geo/Soldier_head BEFORE swat:Hips. A mesh
            // origin is not a skull: using it lifts the entire seated crew.
            foreach(var t in bones)
                if(t!=null&&t.name.ToLowerInvariant().EndsWith("_head")&&t.gameObject.GetComponent<Renderer>()==null&&t.gameObject.GetComponent<MeshFilter>()==null)return t;
            return null;
        }
        private void DisableObsolete(GameObject go)
        {
            if(_disabled.ContainsKey(go))return;
            _disabled.Add(go,go.activeSelf);
            foreach(var aar in go.GetComponentsInChildren<AarVisual>(true))
                if(!_hiddenVisuals.ContainsKey(aar)){_hiddenVisuals.Add(aar,aar.Hidden);aar.Hidden=true;}
            go.SetActive(false);
        }
        private void MoveCrew(Vehicle vehicle,CrewManager.CrewMember member,Transform frame,int role)
        {
            var candidates=new List<Transform>();
            Action<Transform> add=t=>{if(t!=null&&t!=vehicle.transform&&!candidates.Contains(t))candidates.Add(t);};
            var rig=member.AutoBoneRig;
            if(rig!=null){add(rig.transform);if(rig.CharacterRoot!=null)add(rig.CharacterRoot.transform);if(rig.ZonesRoot!=null)add(rig.ZonesRoot.transform);}
            if(member.BodyPartRegistry!=null)add(member.BodyPartRegistry.transform);
            if(member.AnimationTriggers!=null&&member.AnimationTriggers.Animator!=null)add(member.AnimationTriggers.Animator.transform);
            if(member.AllAarVisuals!=null)foreach(var aar in member.AllAarVisuals)if(aar!=null)add(aar.transform);
            var roots=new List<Transform>();
            foreach(var candidate in candidates)
            {bool child=false;foreach(var other in candidates)if(candidate!=other&&candidate.IsChildOf(other)){child=true;break;}if(!child)roots.Add(candidate);}
            var anchor=new GameObject("M8_Crew_"+member.Position);_owned.Add(anchor);
            anchor.transform.SetParent(frame,false);
            Vector3 destination=M8InteriorLayout.CrewHead(role);if(role!=0)destination-=frame.localPosition;
            Vector3 delta=frame.TransformPoint(destination)-Head(member).position;
            foreach(var root in roots){_transforms.Add(new SavedTransform(root));root.SetParent(anchor.transform,true);root.position+=delta;}
            // Animated escaping soldiers may detach; stock M2's static AAR
            // proxy is only hidden on evacuation and must remain vehicle-bound.
            _crew.Add(new SavedCrew{Member=member,Anchor=anchor});
        }
        internal void Maintain()
        {
            // Native X-ray toggles refresh render visibility independently of the
            // removed weapon components. Keep obsolete AAR nodes hidden as well.
            foreach(var pair in _hiddenVisuals)if(pair.Key!=null&&!pair.Key.Hidden)pair.Key.Hidden=true;
            foreach(var pair in _disabled)if(pair.Key!=null&&pair.Key.activeSelf)pair.Key.SetActive(false);
            foreach(var crew in _crew)if(crew.Anchor!=null&&crew.Member.AutoBoneRig!=null&&crew.Member.Evacuated&&crew.Anchor.transform.parent!=null)crew.Anchor.transform.SetParent(null,true);
        }
        internal void Fired(int index)
        {
            if(index<0||index>=12||_rounds[index]==null||!_rounds[index].activeSelf)return;
            if(_fuel[index]!=null)_fuel[index].Expend();
            // Inactive is deliberate: the native collider-group cache may otherwise
            // re-enable a merely disabled collider on the next projectile raycast.
            _rounds[index].SetActive(false);
        }
        private static Mesh MissileMesh(int index)
        {
            const int segments=16;var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int ring=0;ring<2;ring++)for(int i=0;i<segments;i++)vertices.Add(M8InteriorLayout.MissilePoint(index,i*Mathf.PI*2/segments,ring==0?M8InteriorLayout.MissileRear:M8InteriorLayout.MissileTip-.32f,M8InteriorLayout.MissileRadius));
            vertices.Add(M8InteriorLayout.MissilePoint(index,0,M8InteriorLayout.MissileRear,0));
            vertices.Add(M8InteriorLayout.MissilePoint(index,0,M8InteriorLayout.MissileTip,0));
            for(int i=0;i<segments;i++)
            {int n=(i+1)%segments;triangles.AddRange(new[]{i,n,segments+n,i,segments+n,segments+i,32,n,i,segments+i,segments+n,33});}
            var mesh=new Mesh{name="M8_MGM166_Internal_"+index};mesh.vertices=vertices.ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh MissileDisplayMesh(Mesh collision)
        {
            var mesh=UnityEngine.Object.Instantiate(collision);mesh.name=collision.name+"_Display";
            var source=collision.vertices;Vector3 rear=source[32],tip=source[33];
            const int segments=16;
            // Shorter, rounded half-ellipsoid nose: 0.55m rather than the
            // prior 0.65m needle cone. Existing tip, rear, centers and radius stay.
            float[] z={rear.z,tip.z-.55f,tip.z-.275f,tip.z-.11f,tip.z-.0275f,tip.z-.0055f};
            float[] scale={1f,1f,.8660254f,.6f,.3122499f,.1410674f};
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int ring=0;ring<z.Length;ring++)for(int i=0;i<segments;i++)
                vertices.Add(new Vector3(rear.x+(source[i].x-rear.x)*scale[ring],rear.y+(source[i].y-rear.y)*scale[ring],z[ring]));
            for(int ring=0;ring<z.Length-1;ring++)for(int i=0;i<segments;i++)
            {
                int n=(i+1)%segments,a=ring*segments,b=(ring+1)*segments;
                triangles.AddRange(new[]{a+i,a+n,b+n,a+i,b+n,b+i});
            }
            int nose=vertices.Count;vertices.Add(tip);
            for(int i=0;i<segments;i++)triangles.AddRange(new[]{(z.Length-1)*segments+i,(z.Length-1)*segments+(i+1)%segments,nose});
            // Separate the planar cap rim from the body rim so recalculated
            // normals cannot round the rear face or make it look tapered.
            int cap=vertices.Count;for(int i=0;i<segments;i++)vertices.Add(source[i]);
            int center=vertices.Count;vertices.Add(rear);
            for(int i=0;i<segments;i++)triangles.AddRange(new[]{center,cap+(i+1)%segments,cap+i});
            mesh.vertices=vertices.ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            for(int i=0;i<_handlers.Count;i++)if(MissileDamage[i]!=null)MissileDamage[i].Destroyed-=_handlers[i];
            for(int i=0;i<_burnHandlers.Count;i++)if(_fuel[i]!=null)_fuel[i].Ignited-=_burnHandlers[i];
            if(_externalFire!=null)_externalFire.Dispose();
            foreach(var go in _owned)if(go!=null)go.SetActive(false);
            if(_group!=null&&_oldGroupColliders!=null)Set(_group,"childColliders",_oldGroupColliders);
            for(int i=_transforms.Count-1;i>=0;i--)_transforms[i].Restore();
            foreach(var s in _meshStates){if(s.Filter!=null)s.Filter.sharedMesh=s.Visual;if(s.Collider!=null){s.Collider.sharedMesh=null;s.Collider.sharedMesh=s.Collision;}}
            foreach(var pair in _disabled)if(pair.Key!=null)pair.Key.SetActive(pair.Value);
            foreach(var pair in _hiddenVisuals)if(pair.Key!=null)pair.Key.Hidden=pair.Value;
            foreach(var pair in _clusters)if(pair.Key!=null){pair.Key.Items=pair.Value;pair.Key.RefreshExplosivesStatus();pair.Key.RefreshUndetonatedExplosivesStatus();}
            foreach(var go in _owned)if(go!=null)UnityEngine.Object.Destroy(go);
            foreach(var mesh in _meshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);
            if(_missileMaterial!=null)UnityEngine.Object.Destroy(_missileMaterial);
        }
    }
}
