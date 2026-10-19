using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using UnityEngine;
namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private struct ViewportScanCandidate
        {internal Vehicle Vehicle;internal float Score,Range;internal int Id;}
        private readonly List<ViewportScanCandidate> _viewportScanCandidates=new List<ViewportScanCandidate>(64);
        private static readonly Comparison<ViewportScanCandidate> ViewportScanOrder=CompareViewportScanCandidates;
        private OpticsUpgrade _viewportScanOwner;
        private UnityEngine.Camera _viewportScanCamera;
        private Quaternion _viewportScanRotation;
        private float _viewportScanFov,_viewportScanDeadline;
        private int _viewportScanCursor;
        private bool ViewportTargetScanPending {get {return _viewportScanOwner!=null;}}
        private static int CompareViewportScanCandidates(ViewportScanCandidate a,ViewportScanCandidate b)
        {
            int order=a.Score.CompareTo(b.Score);if(order!=0) return order;
            order=a.Range.CompareTo(b.Range);return order!=0 ? order : a.Id.CompareTo(b.Id);
        }
        private bool AlreadyTargetLocked(Vehicle vehicle)
        {foreach(TargetLock target in _targetLocks) if(target.Vehicle==vehicle) return true;return false;}
        private void CancelViewportTargetScan()
        {
            _viewportScanCandidates.Clear();_viewportScanOwner=null;_viewportScanCamera=null;
            _viewportScanCursor=0;
        }
        private void BeginViewportTargetScan(OpticsUpgrade owner,UnityEngine.Camera camera)
        {
            CancelViewportTargetScan();
            if(owner==null || M8SightDamaged(owner))return;
            if(owner!=null && camera!=null) BeginSightScanPulse(owner);
            if(owner==null || camera==null || _targetLocks.Count>=TargetLockRules.MaximumTargets) return;
            // Snapshot the existing discovery cache once on the key-down event.
            // No scene-wide FindObjects call and no automatic scan restart.
            foreach(Vehicle vehicle in _lockCandidates)
            {
                if(vehicle==owner.Vehicle || !LockCandidateAlive(vehicle) || AlreadyTargetLocked(vehicle)) continue;
                Vector3 point=vehicle.transform.position;
                float range=Vector3.Distance(camera.transform.position,point);
                Vector3 view=camera.WorldToViewportPoint(point);
                if(!ViewportScanRules.CandidateInView(view.x,view.y,view.z,range)) continue;
                _viewportScanCandidates.Add(new ViewportScanCandidate {Vehicle=vehicle,Score=ViewportScanRules.CenterScore(view.x,view.y),Range=range,Id=vehicle.GetInstanceID()});
            }
            if(_viewportScanCandidates.Count==0) return;
            _viewportScanCandidates.Sort(ViewportScanOrder);
            _viewportScanOwner=owner;_viewportScanCamera=camera;_viewportScanRotation=camera.transform.rotation;
            _viewportScanFov=camera.fieldOfView;_viewportScanDeadline=Time.unscaledTime+ViewportScanRules.ScanLifetime;
        }
        private void UpdateViewportTargetScan(OpticsUpgrade owner,UnityEngine.Camera camera)
        {
            if(!ViewportTargetScanPending) return;
            if(M8SightDamaged(owner) || owner!=_viewportScanOwner || camera==null || camera!=_viewportScanCamera || CurrentLockSight()!=owner ||
                !LockInputReady() || Time.unscaledTime>_viewportScanDeadline ||
                !ViewportScanRules.SameView(Quaternion.Angle(camera.transform.rotation,_viewportScanRotation),camera.fieldOfView-_viewportScanFov))
            {CancelViewportTargetScan();return;}
            int tested=0,visibility=0;
            while(_viewportScanCursor<_viewportScanCandidates.Count && _targetLocks.Count<TargetLockRules.MaximumTargets &&
                tested<ViewportScanRules.CandidateChecksPerFrame && visibility<ViewportScanRules.VisibilityChecksPerFrame)
            {
                Vehicle vehicle=_viewportScanCandidates[_viewportScanCursor++].Vehicle;tested++;
                if(vehicle==owner.Vehicle || !LockCandidateAlive(vehicle) || AlreadyTargetLocked(vehicle)) continue;
                Bounds body=CenterBodyBounds(vehicle);Vector3 point=vehicle.transform.TransformPoint(body.center);
                Vector3 view=camera.WorldToViewportPoint(point);
                if(!TargetLockRules.InView(view.x,view.y,view.z,Vector3.Distance(camera.transform.position,point))) continue;
                visibility++;
                // Reuse the existing terrain/thermal-smoke and partial-body LOS
                // policy. No ground-wall bypass and no enemy discovery through smoke.
                if(!TargetBodyVisible(camera,vehicle,owner.Vehicle,body)) continue;
                // One queued, non-overlapping confirmation per NEW successful lock.
                AddTargetLock(owner,camera,vehicle,body,true);
            }
            if(_targetLocks.Count>=TargetLockRules.MaximumTargets || _viewportScanCursor>=_viewportScanCandidates.Count)
                CancelViewportTargetScan();
        }
    }
}
