using System.Collections.Generic;
using GHPC;
using GHPC.Vehicle;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private readonly List<Vehicle> _lockCandidates=new List<Vehicle>();
        private readonly HashSet<Vehicle> _lockCandidateSet=new HashSet<Vehicle>();
        private readonly Dictionary<IUnit,Vehicle> _lockVehicleByUnit=new Dictionary<IUnit,Vehicle>();
        private readonly Dictionary<Collider,Vehicle> _lockColliderOwners=new Dictionary<Collider,Vehicle>();
        private readonly Dictionary<Vehicle,Bounds> _lockBodyBounds=new Dictionary<Vehicle,Bounds>();
        private sealed class BodyProbe {internal int Cursor=1,VisibleA=-1,VisibleB=-1;}
        private readonly Dictionary<Vehicle,BodyProbe> _bodyProbes=new Dictionary<Vehicle,BodyProbe>(16);
        private BodyProbe BodyProbeFor(Vehicle target)
        {
            BodyProbe probe;
            if(!_bodyProbes.TryGetValue(target,out probe)) {_bodyProbes[target]=probe=new BodyProbe();}
            return probe;
        }
        private void RefreshLockCandidates(Vehicle[] vehicles)
        {
            foreach(Vehicle v in vehicles) RememberLockCandidate(v);
        }
        private void RememberLockCandidate(Vehicle v)
        {
            if(!LockCandidateAlive(v)) return;
            if(_lockCandidateSet.Add(v)) _lockCandidates.Add(v);
            _lockVehicleByUnit[v.BaseUnit]=v;
        }
        private void ForgetLockCandidate(Vehicle v)
        {
            if(object.ReferenceEquals(v,null)) return;
            _lockCandidateSet.Remove(v);_lockCandidates.Remove(v);_lockBodyBounds.Remove(v);_bodyProbes.Remove(v);
            if(v.BaseUnit!=null) _lockVehicleByUnit.Remove(v.BaseUnit);
        }
        private Vehicle ResolveLockCollider(Collider collider)
        {
            Vehicle v;
            if(_lockColliderOwners.TryGetValue(collider,out v)) return v;
            v=collider.GetComponentInParent<Vehicle>();
            if(v==null)
            {
                // Armor can be reparented outside the Vehicle hierarchy.
                UniformArmor armor=collider.GetComponentInParent<UniformArmor>();
                if(armor!=null && armor.Unit!=null) _lockVehicleByUnit.TryGetValue(armor.Unit,out v);
            }
            if(v!=null) _lockColliderOwners[collider]=v;return v;
        }
        private Bounds CenterBodyBounds(Vehicle target)
        {
            Bounds bounds;if(_lockBodyBounds.TryGetValue(target,out bounds)) return bounds;
            Renderer best=null;float bestVolume=0;
            foreach(Renderer r in target.GetComponentsInChildren<Renderer>())
            {
                if(!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                string n=r.name.ToLowerInvariant();
                if(!(n.Contains("hull") || n.Contains("body") || n.Contains("chassis")) ||
                    n.Contains("crew") || n.Contains("aar") || n.Contains("track") || n.Contains("barrel")) continue;
                Vector3 size=r.bounds.size;float volume=size.x*size.y*size.z;
                if(volume>bestVolume && volume<300f && (r.bounds.center-target.transform.position).sqrMagnitude<100f)
                {bestVolume=volume;best=r;}
            }
            if(best!=null)
            {
                Bounds world=best.bounds;bool first=true;bounds=new Bounds();
                for(int i=0;i<8;i++)
                {
                    Vector3 p=target.transform.InverseTransformPoint(world.center+Vector3.Scale(world.extents,
                        new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(first) {bounds=new Bounds(p,Vector3.zero);first=false;} else bounds.Encapsulate(p);
                }
            }
            else
            {
                bounds=TargetLocalBounds(target);
                // Exclude gun/antenna tails from the fallback's longitudinal center.
                Vector3 center=bounds.center;center.x=Mathf.Clamp(center.x,-.45f,.45f);
                center.z=Mathf.Clamp(center.z,-.65f,.65f);center.y=bounds.min.y+bounds.size.y*.45f;
                bounds.center=center;
            }
            _lockBodyBounds[target]=bounds;BodyProbeFor(target);return bounds; // Prewarm on acquisition, not every recovery tick.
        }
        private bool TargetBodyVisible(UnityEngine.Camera camera,Vehicle target,Vehicle self,Bounds body)
        {bool hardBlocked;return camera!=null && TargetBodyVisibilityFrom(camera.transform.position,target,self,body,out hardBlocked);}
        private bool TargetBodyVisibilityFrom(Vector3 origin,Vehicle target,Vehicle self,Bounds body,out bool hardBlocked)
        {return TargetBodyVisibilityFrom(origin,target,self,body,false,out hardBlocked);}
        private bool TargetBodyVisibilityFrom(Vector3 origin,Vehicle target,Vehicle self,Bounds body,bool recovery,out bool hardBlocked)
        {
            _lockBlockers.Clear();_lockVisibilityBatch=true;
            try {return ProbeBodyVisibility(origin,target,self,body,recovery,out hardBlocked);}
            finally {_lockVisibilityBatch=false;}
        }
        private bool ProbeBodyVisibility(Vector3 origin,Vehicle target,Vehicle self,Bounds body,bool recovery,out bool hardBlocked)
        {
            // Visibility is not the impact aim point. An exposed roof/side can
            // be acquired even if the center is temporarily behind a low wall.
            bool sample;
            bool visible=TargetVisibleFrom(origin,target,self,target.transform.TransformPoint(body.center),out sample);
            hardBlocked=sample;if(visible) {hardBlocked=false;return true;}
            // A sparse edge gap must not defeat IR smoke over the body center.
            // Real cover still uses roof/side samples for partial visibility.
            bool thermal=_lastRayThermalBlocked;
            if(thermal && !recovery) {hardBlocked=true;return false;}
            if(recovery) return ProbeRecoveryBody(origin,target,self,body,thermal,hardBlocked,out hardBlocked);
            int clearSamples=0;
            Vector3 e=body.extents*.75f;
            for(int i=0;i<5;i++)
            {
                Vector3 offset=i==0 ? new Vector3(0,e.y,0) :
                    i==1 ? new Vector3(e.x,e.y*.4f,0) : i==2 ? new Vector3(-e.x,e.y*.4f,0) :
                    i==3 ? new Vector3(0,e.y*.4f,e.z) : new Vector3(0,e.y*.4f,-e.z);
                visible=TargetVisibleFrom(origin,target,self,target.transform.TransformPoint(body.center+offset),out sample);
                hardBlocked &= sample;
                if(visible && (!thermal || ++clearSamples>=2)) {hardBlocked=false;return true;}
            }
            if(thermal) hardBlocked=true;
            return false; // Real terrain/other vehicles still block all samples.
        }
        private bool ProbeRecoveryBody(Vector3 origin,Vehicle target,Vehicle self,Bounds body,bool thermal,bool centerBlocked,out bool hardBlocked)
        {
            BodyProbe probe=BodyProbeFor(target);int previousA=probe.VisibleA,previousB=probe.VisibleB;
            int visibleA=-1,visibleB=-1;hardBlocked=centerBlocked;
            // Center + at most six other points per check (7 total, formerly 14).
            // Last exposed points are retested first so rolling the search does
            // not interrupt the 0.2 s visibility settling of a moving target.
            for(int attempt=0;attempt<6;attempt++)
            {
                int index;
                if(attempt==0 && previousA>0) index=previousA;
                else if(attempt==1 && previousB>0 && previousB!=previousA) index=previousB;
                else
                {
                    index=probe.Cursor;probe.Cursor=probe.Cursor==13 ? 1 : probe.Cursor+1;
                    if(index==previousA || index==previousB) {attempt--;continue;}
                }
                Vector3 offset=BodyProbeOffset(body.extents,index);bool blocked;
                bool visible=TargetVisibleFrom(origin,target,self,target.transform.TransformPoint(body.center+offset),out blocked);
                hardBlocked &= blocked;
                if(!visible) continue;
                if(visibleA<0) visibleA=index;else if(index!=visibleA) visibleB=index;
                probe.VisibleA=visibleA;probe.VisibleB=visibleB;
                if(!thermal || visibleB>0) {hardBlocked=false;return true;}
            }
            probe.VisibleA=visibleA;probe.VisibleB=visibleB;
            if(thermal) hardBlocked=true;
            return false;
        }
        private static Vector3 BodyProbeOffset(Vector3 extents,int index)
        {
            Vector3 e=extents*.75f;
            switch(index)
            {
                case 1:return new Vector3(0,e.y,0);
                case 2:return new Vector3(e.x,e.y*.4f,0);
                case 3:return new Vector3(-e.x,e.y*.4f,0);
                case 4:return new Vector3(0,e.y*.4f,e.z);
                case 5:return new Vector3(0,e.y*.4f,-e.z);
                default:int i=index-6;return new Vector3(extents.x*LockRecoveryRules.SearchX(i),
                    extents.y*LockRecoveryRules.SearchY(i),extents.z*LockRecoveryRules.SearchZ(i));
            }
        }
        private Vehicle PickCenterNeighbor(UnityEngine.Camera camera,OpticsUpgrade owner,Vehicle direct)
        {
            // Only on middle-button point selection. HUD rectangles do not capture input and their size
            // does not determine which vehicle is acquired.
            if(direct!=null)
            {
                bool locked=false;
                foreach(TargetLock t in _targetLocks) if(t.Vehicle==direct) {locked=true;break;}
                if(!locked) return direct; // Real, unlocked body hit wins over proximity.
            }
            Vehicle best=direct;float score=float.PositiveInfinity;
            Vector2 center=new Vector2(camera.pixelRect.center.x,camera.pixelRect.center.y);
            if(direct!=null)
            {
                Vector3 screen=camera.WorldToScreenPoint(direct.transform.TransformPoint(CenterBodyBounds(direct).center));
                score=(new Vector2(screen.x,screen.y)-center).sqrMagnitude;
            }
            foreach(Vehicle v in _lockCandidates)
            {
                if(v==owner.Vehicle || !LockCandidateAlive(v) || v==direct ||
                    (v.transform.position-camera.transform.position).sqrMagnitude>TargetLockRules.MaximumRange*TargetLockRules.MaximumRange) continue;
                bool alreadyLocked=false;
                foreach(TargetLock t in _targetLocks) if(t.Vehicle==v) {alreadyLocked=true;break;}
                if(alreadyLocked) continue;
                Vector3 approximate=camera.WorldToScreenPoint(v.transform.position);
                if(approximate.z<=0 || (new Vector2(approximate.x,approximate.y)-center).sqrMagnitude>10000f) continue;
                Bounds body=CenterBodyBounds(v);Vector3 point=v.transform.TransformPoint(body.center);
                Vector3 screen=camera.WorldToScreenPoint(point);
                float distance=(new Vector2(screen.x,screen.y)-center).sqrMagnitude;
                if(screen.z<=0 || !TargetPickRules.PreferNeighbor(distance,score)) continue;
                if(!TargetBodyVisible(camera,v,owner.Vehicle,body)) continue;
                best=v;score=distance;
            }
            return best;
        }
        private void ResetCenterLockCaches()
        {_lockCandidates.Clear();_lockCandidateSet.Clear();_lockVehicleByUnit.Clear();_lockColliderOwners.Clear();_lockBodyBounds.Clear();_bodyProbes.Clear();_lockBlockers.Clear();_lockVisibilityBatch=false;_nextSmokeOrigin=0f;_inThermalSmoke=false;}
    }
}
