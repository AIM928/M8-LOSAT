using System;
using System.Collections.Generic;
using GHPC;
using GHPC.Camera;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace M8LOSATM2.LosatCore
{
    internal sealed partial class LosatService
    {
        private sealed class TargetLock
        {
            internal Vehicle Vehicle;
            internal Bounds LocalBounds;
            internal Vector3 LocalAimPoint;
            internal Rect ScreenRect;
            // Presentation snapshot: never used for picking, LOS or missile guidance.
            internal Rect RenderScreenRect;
            internal bool RenderOnScreen;
            internal int RenderFrame=-2;
            internal bool Visible;
            internal bool OnScreen;
            internal float ObscuredSince=-1f;
            internal float ClearSince=-1f;
            internal bool Reacquiring;
            internal float NextVisibility;
            internal string Caption;
            internal int RangeMeters=-1;
            internal TargetAttackState Attack=new TargetAttackState();
            internal TargetLock OriginalSelection;
        }
        private readonly List<TargetLock> _targetLocks=new List<TargetLock>(TargetLockRules.MaximumTargets);
        private readonly RaycastHit[] _lockHits=new RaycastHit[256];
        private OpticsUpgrade _lockOwner;
        private GUIStyle _lockStyle;
        private readonly TargetSalvoPlan _targetSalvo=new TargetSalvoPlan();
        private readonly TargetLock[] _salvoAssignments=new TargetLock[TargetLockRules.MaximumTargets];
        private WeaponSystem _salvoWeapon;
        private WeaponSystem _dispatchWeapon;
        private TargetLock _dispatchTarget;
        private bool _dispatchSucceeded;
        private bool _lastRayThermalBlocked;

        private void InstallTargetLockHooks()
        {
            InstallSmokeSourceHook();
            HarmonyInstance.Patch(AccessTools.Method(typeof(LiveRound),"penCheck"),
                postfix:new HarmonyMethod(typeof(LosatService),nameof(AssignedTargetImpactPostfix)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(MissileGuidanceUnit),"PerformGuidance"),
                postfix:new HarmonyMethod(typeof(LosatService),nameof(LockedGuidancePostfix)) {priority=HarmonyLib.Priority.Last});
            HarmonyInstance.Patch(AccessTools.Method(typeof(CameraManager),"ToggleCameraSlotSet"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(LockMirrorTogglePrefix)),
                postfix:new HarmonyMethod(typeof(LosatService),nameof(LockMirrorTogglePostfix)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(CameraManager),"CycleCameraSlot"),
                prefix:new HarmonyMethod(typeof(LosatService),nameof(LockCrewViewCyclePrefix)));
        }
        private void ClearTargetLock()
        {
            ClearSightFeedback();
            CancelViewportTargetScan();
            _targetLocks.Clear();_lockOwner=null;_selectedLock=null;CancelTargetSalvo();
            _lockMirrorReturnActive=false;_lockMirrorReturnSlot=null;
            // In-flight assignments belong to TrackedMissile and survive this clear.
        }
        private void CancelTargetSalvo()
        {
            _targetSalvo.Cancel();_salvoWeapon=null;_dispatchWeapon=null;_dispatchTarget=null;
            Array.Clear(_salvoAssignments,0,_salvoAssignments.Length);
        }
        private static bool LockCandidateAlive(Vehicle vehicle)
        {
            return vehicle!=null && vehicle.gameObject.activeInHierarchy &&
                vehicle.gameObject.scene.IsValid() && vehicle.BaseUnit!=null &&
                vehicle.BaseUnit.Targetable && !vehicle.BaseUnit.Neutralized;
        }
        private Vehicle RayVehicle(Vector3 origin,Vector3 direction,float distance,Vehicle self,out bool clear)
        {bool hardBlocked;return RayVehicle(origin,direction,distance,self,out clear,out hardBlocked);}
        private Vehicle RayVehicle(Vector3 origin,Vector3 direction,float distance,Vehicle self,out bool clear,out bool hardBlocked)
        {
            clear=false;hardBlocked=false;_lastRayThermalBlocked=false;
            if(ThermalSmokeAtOrigin(origin)) {hardBlocked=true;_lastRayThermalBlocked=true;return null;}
            int count=Physics.RaycastNonAlloc(origin,direction,_lockHits,distance,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Collide);
            if(count==_lockHits.Length) return null;
            float nearest=float.PositiveInfinity;Vehicle hitVehicle=null;
            for(int i=0;i<count;i++)
            {
                // A farther hit cannot replace the nearest confirmed blocker.
                // Avoid walking its component hierarchy during dense blasts.
                if(_lockHits[i].distance>=nearest) continue;
                Collider collider=_lockHits[i].collider;
                if(collider==null) continue;
                LockBlocker kind;Vehicle owner=LockRayOwner(collider,out kind);
                if(owner==self || (owner==null && kind==LockBlocker.Ignore)) continue;
                if(_lockHits[i].distance<nearest) {nearest=_lockHits[i].distance;hitVehicle=owner;hardBlocked=owner==null;_lastRayThermalBlocked=kind==LockBlocker.ThermalSmoke;}
            }
            clear=float.IsPositiveInfinity(nearest);
            return hitVehicle;
        }
        private bool TargetVisible(UnityEngine.Camera camera,Vehicle target,Vehicle self,Vector3 point)
        {
            if(camera==null) return false;
            bool hardBlocked;return TargetVisibleFrom(camera.transform.position,target,self,point,out hardBlocked);
        }
        private bool TargetVisibleFrom(Vector3 origin,Vehicle target,Vehicle self,Vector3 point,out bool hardBlocked)
        {
            Vector3 direction=point-origin;bool clear;
            Vehicle hit=RayVehicle(origin,direction.normalized,direction.magnitude+.1f,self,out clear,out hardBlocked);
            return clear || hit==target;
        }
        private static Bounds TargetLocalBounds(Vehicle target)
        {
            Bounds bounds=new Bounds();bool found=false;Transform root=target.transform;
            foreach(Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                if(!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) ||
                    renderer.name=="TRACKING OBJECT") continue;
                Bounds world=renderer.bounds;
                if(world.size.sqrMagnitude>.01f && world.size.sqrMagnitude<900f &&
                    (world.center-root.position).sqrMagnitude<400f)
                    for(int i=0;i<8;i++)
                    {
                        Vector3 corner=root.InverseTransformPoint(world.center+Vector3.Scale(world.extents,
                            new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                        if(!found) {bounds=new Bounds(corner,Vector3.zero);found=true;} else bounds.Encapsulate(corner);
                    }
            }
            if(!found) bounds=new Bounds(new Vector3(0,1,0),new Vector3(3,2,5));
            return bounds;
        }
        private void AcquireTargetLock(OpticsUpgrade owner,UnityEngine.Camera camera)
        {
            if(owner==null || camera==null || M8SightDamaged(owner))return;
            // A lost frame has no live collider to hit. Cancel it at its last
            // visible screen position, including the blink's off phase.
            Vector2 reticle=new Vector2(camera.pixelRect.center.x,Screen.height-camera.pixelRect.center.y);
            for(int i=0;i<_targetLocks.Count;i++)
            {
                TargetLock lost=_targetLocks[i];Rect frame=LockDisplayRect(lost);
                if(lost.Reacquiring && lost.OnScreen && reticle.x>=frame.x-12f && reticle.x<=frame.xMax+12f &&
                    reticle.y>=frame.y-12f && reticle.y<=frame.yMax+12f)
                {_targetLocks.RemoveAt(i);CancelTargetSalvo();return;}
            }
            Ray ray=camera.ViewportPointToRay(new Vector3(.5f,.5f,0));bool clear;
            Vehicle target=RayVehicle(ray.origin,ray.direction,TargetLockRules.MaximumRange,owner.Vehicle,out clear);
            target=PickCenterNeighbor(camera,owner,target);
            if(target==null && _selectedLock!=null && _selectedLock.Reacquiring)
            {_targetLocks.Remove(_selectedLock);CancelTargetSalvo();return;}
            if(!LockCandidateAlive(target) || target==owner.Vehicle) return;
            for(int i=0;i<_targetLocks.Count;i++)
                if(_targetLocks[i].Vehicle==target) {_targetLocks.RemoveAt(i);return;}
            if(_targetLocks.Count>=TargetLockRules.MaximumTargets) return;
            Bounds bounds=CenterBodyBounds(target);
            Vector3 point=target.transform.TransformPoint(bounds.center);
            Vector3 view=camera.WorldToViewportPoint(point);
            if(!TargetLockRules.InView(view.x,view.y,view.z,(point-camera.transform.position).magnitude) ||
                !TargetBodyVisible(camera,target,owner.Vehicle,bounds)) return;
            AddTargetLock(owner,camera,target,bounds,true);
        }
        private void AddTargetLock(OpticsUpgrade owner,UnityEngine.Camera camera,Vehicle target,Bounds bounds,bool confirm)
        {
            Vector3 point=target.transform.TransformPoint(bounds.center);
            _lockOwner=owner;
            _selectedLock=new TargetLock {Vehicle=target,LocalBounds=bounds,LocalAimPoint=bounds.center,
                Visible=true,Caption=""};
            _targetLocks.Add(_selectedLock);
            RefreshLockRange(_selectedLock,camera,point);
            if(confirm) PlayLockConfirmation();
            // Desynchronize the four visibility checks instead of batching
            // every obscured target's physics queries on the same frame.
            _selectedLock.NextVisibility=Time.unscaledTime+(_targetLocks.Count-1)*TargetLockRules.VisibilityInterval*.25f;
            if(ShotDiagnostics) MelonLogger.Msg(Prefix+"LOCK "+_targetLocks.Count+"/"+TargetLockRules.MaximumTargets+" "+target.BaseUnit.FriendlyName);
        }
        private void UpdateTargetLock()
        {
            if(Input.GetKeyDown(KeyCode.Backspace) && (_targetLocks.Count>0 || _targetSalvo.Pending || ViewportTargetScanPending || _sightFeedbackOwner!=null))
            {
                // Also works outside the sight. Does not touch airborne assignments.
                ClearTargetLock();
                if(ShotDiagnostics) MelonLogger.Msg(Prefix+"LOCK cleared: all unlaunched targets and queued shots cancelled (Backspace).");
                return;
            }
            OpticsUpgrade ownSight=CurrentLockViewContext();UnityEngine.Camera camera=CameraManager.MainCam;
            if(ownSight==null || (_lockOwner!=null && ownSight!=_lockOwner))
            {ClearTargetLock();return;}
            if(M8SightDamaged(ownSight)){ClearTargetLock();return;}
            // Closing the optic is not changing crew/weapon view. Keep locks,
            // but only acquire or draw through the actual optic. Menu/pause is
            // also not a view change and must not clear an existing selection.
            if(camera==null || !LockInputReady()) {CancelViewportTargetScan();return;}
            if(CurrentLockSight()!=null)
            {
                // Manual point selection wins if both buttons arrive together.
                // It cancels a pending scan so an unlocked target is not re-added.
                if(Input.GetMouseButtonDown(2)) {CancelViewportTargetScan();ClearSightFeedback();AcquireTargetLock(ownSight,camera);}
                else if(Input.GetKeyDown(KeyCode.E)) BeginViewportTargetScan(ownSight,camera);
            }
            else CancelViewportTargetScan();
            for(int i=_targetLocks.Count-1;i>=0;i--)
            {
                TargetLock target=_targetLocks[i];bool check=Time.unscaledTime>=target.NextVisibility;
                if(check) target.NextVisibility=Time.unscaledTime+TargetLockRules.VisibilityInterval;
                if(!UpdateTrackedTarget(_targetLocks[i],camera,ownSight.Vehicle,check))
                    _targetLocks.RemoveAt(i);
            }
            if(_selectedLock==null || !_targetLocks.Contains(_selectedLock))
                _selectedLock=_targetLocks.Count==0 ? null : _targetLocks[_targetLocks.Count-1];
            UpdateViewportTargetScan(ownSight,camera);
        }
        private static Rect LockDisplayRect(TargetLock target)
        {
            Rect r=target.ScreenRect;
            if(!target.Reacquiring) return r;
            float x=LockRecoveryRules.SearchPadding(r.width),y=LockRecoveryRules.SearchPadding(r.height);
            return new Rect(r.x-x,r.y-y,r.width+2f*x,r.height+2f*y);
        }
        private static void RefreshLockRange(TargetLock target,UnityEngine.Camera camera,Vector3 point)
        {
            int meters=Mathf.RoundToInt(Vector3.Distance(camera.transform.position,point));
            if(meters==target.RangeMeters) return;
            target.RangeMeters=meters;
            target.Caption=meters.ToString(System.Globalization.CultureInfo.InvariantCulture)+" m";
        }

        private bool BeginTargetSalvo(SalvoWeapon state)
        {
            if(_targetSalvo.Pending || state.Guidance.Available==0 || !LockInputReady() || M8SightDamaged(_lockOwner) || CurrentLockViewContext()!=_lockOwner || _targetLocks.Count==0) return false;
            int[] ids=new int[_targetLocks.Count];int n=0;
            for(int i=0;i<_targetLocks.Count && n<TargetSalvoPlan.MaximumShots;i++)
            {
                TargetLock target=_targetLocks[i];
                if(target.Attack.InFlight || !target.Visible || !LockCandidateAlive(target.Vehicle) ||
                    !TargetInLaunchSector(state,target.Vehicle.transform.TransformPoint(target.LocalAimPoint))) continue;
                _salvoAssignments[n]=new TargetLock {Vehicle=target.Vehicle,LocalBounds=target.LocalBounds,LocalAimPoint=target.LocalAimPoint,Visible=true,
                    OriginalSelection=target,Attack=target.Attack};
                ids[n++]=target.Vehicle.GetInstanceID();
            }
            Array.Resize(ref ids,n);
            if(!_targetSalvo.Begin(ids,Math.Min(state.Weapon.Feed.CurrentClipRemainingCount,state.Guidance.Available),Time.time))
            {Array.Clear(_salvoAssignments,0,_salvoAssignments.Length);return false;}
            _salvoWeapon=state.Weapon;state.Sequence.SuccessfulShot();
            if(_targetSalvo.Count<Math.Min(n,TargetSalvoPlan.MaximumShots))
                MelonLogger.Warning(Prefix+"TARGET_SALVO limited by ready ammunition: "+_targetSalvo.Count+"/"+n+" targets.");
            if(ShotDiagnostics) MelonLogger.Msg(Prefix+"TARGET_SALVO one click, "+_targetSalvo.Count+" missiles assigned to distinct targets.");
            return true;
        }
        private void UpdateTargetSalvo()
        {
            if(!_targetSalvo.Pending) return;
            OpticsUpgrade sight=CurrentLockViewContext();
            if(sight==null || M8SightDamaged(sight) || sight.Weapon!=_salvoWeapon || _salvoWeapon.Feed==null)
            {CancelTargetSalvo();return;}
            // Keep acquired locks during menu/pause, but never leave a burst
            // queued to surprise-fire when controls become live again.
            if(!LockInputReady()) {CancelTargetSalvo();return;}
            if(CameraManager.MainCam==null) return;
            if(_targetSalvo.Expired(Time.time))
            {MelonLogger.Warning(Prefix+"TARGET_SALVO target skipped: native launcher could not aim/fire within 3 seconds.");SkipSalvoTarget();return;}
            TargetLock target=_salvoAssignments[_targetSalvo.Index];
            if(target==null || !LockCandidateAlive(target.Vehicle)) {SkipSalvoTarget();return;}
            if(target.Attack.InFlight) {SkipSalvoTarget();return;}
            if(target.Vehicle.GetInstanceID()!=_targetSalvo.TargetId) {CancelTargetSalvo();return;}
            Vector3 point=target.Vehicle.transform.TransformPoint(target.LocalAimPoint);
            SalvoWeapon sectorState;
            if(!SalvoWeapons.TryGetValue(_salvoWeapon,out sectorState)) {CancelTargetSalvo();return;}
            if(sectorState.Guidance.Available==0) return;
            if(!TargetInLaunchSector(sectorState,point)) {SkipSalvoTarget();return;}
            if(Time.unscaledTime>=target.NextVisibility)
            {
                target.NextVisibility=Time.unscaledTime+TargetLockRules.VisibilityInterval;
                target.Visible=TargetBodyVisible(CameraManager.MainCam,target.Vehicle,sight.Vehicle,target.LocalBounds);
            }
            if(!target.Visible) {SkipSalvoTarget();return;}
            FireControlSystem fcs=_salvoWeapon.FCS;
            fcs.NotifyAimOverride();fcs.SetAimWorldPosition(point);
            if(!_targetSalvo.CanAttempt(Time.time) || !_salvoWeapon.AbleToFire || fcs.FiringBlocked) return;
            SynchronizeLauncher(_salvoWeapon);
            Transform muzzle=NativeSightAim(_salvoWeapon);
            if(muzzle==null || Vector3.Angle(muzzle.forward,point-muzzle.position)>.75f) return;
            SalvoWeapon state;
            if(!SalvoWeapons.TryGetValue(_salvoWeapon,out state)) {CancelTargetSalvo();return;}
            _dispatchSucceeded=false;
            try
            {
                _dispatchWeapon=_salvoWeapon;_dispatchTarget=target;
                // Native Fire owns ammunition, barrel rotation, damage and cooldown.
                _salvoWeapon.Fire(target.Vehicle.BaseUnit);
            }
            catch(Exception ex)
            {
                CancelTargetSalvo();
                MelonLogger.Error(Prefix+"TARGET_SALVO cancelled safely: "+ex);
                return;
            }
            finally {_dispatchWeapon=null;_dispatchTarget=null;}
            if(_dispatchSucceeded) _targetSalvo.SuccessfulShot(Time.time);
            if(!_targetSalvo.Pending) CancelTargetSalvo();
        }
        private void SkipSalvoTarget()
        {
            _targetSalvo.Skip(Time.time);
            if(!_targetSalvo.Pending) CancelTargetSalvo();
        }
        private void BindDispatchedMissile(LiveRound round,WeaponSystem weapon)
        {
            if(round==null || _dispatchWeapon!=weapon || _dispatchTarget==null) return;
            _dispatchSucceeded=true; // Native Fired event acknowledges exactly one physical launch.
            foreach(TrackedMissile tracked in _missiles)
                if(tracked.Round==round && tracked.RoundId==round.ID)
                {
                    tracked.TargetAssignment=new MissileTargetAssignment(round.ID,_dispatchTarget.Vehicle.GetInstanceID());
                    if(_dispatchTarget.Attack.Begin(round.ID)) tracked.AttackSelection=_dispatchTarget.OriginalSelection;
                    tracked.UseShallowArc=true;
                    tracked.TargetVehicle=_dispatchTarget.Vehicle;
                    tracked.TargetLocalPoint=_dispatchTarget.LocalAimPoint;tracked.TargetGuidance=weapon.GuidanceUnit;
                    tracked.LastTargetPosition=tracked.TargetVehicle.transform.TransformPoint(tracked.TargetLocalPoint);
                    round.GoalVector=(tracked.LastTargetPosition-round.transform.position).normalized;
                    break;
                }
        }
        private static void ProjectLockFrame(UnityEngine.Camera camera,TargetLock target)
        {
            Vector3 center=camera.WorldToViewportPoint(target.Vehicle.transform.TransformPoint(target.LocalAimPoint));
            target.OnScreen=center.z>0f && center.x>=0f && center.x<=1f && center.y>=0f && center.y<=1f;
            if(!target.OnScreen) return;
            float minX=float.PositiveInfinity,minY=float.PositiveInfinity,maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
            for(int i=0;i<8;i++)
            {
                Vector3 local=target.LocalBounds.center+Vector3.Scale(target.LocalBounds.extents,
                    new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Vector3 p=camera.WorldToScreenPoint(target.Vehicle.transform.TransformPoint(local));
                if(p.z<=0f) continue;
                minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);
                minY=Mathf.Min(minY,Screen.height-p.y);maxY=Mathf.Max(maxY,Screen.height-p.y);
            }
            if(float.IsInfinity(minX) || float.IsInfinity(minY)) {target.OnScreen=false;return;}
            float w=SightHudRules.FrameWidth(maxX-minX),h=SightHudRules.FrameHeight(maxY-minY);
            target.ScreenRect=new Rect((minX+maxX-w)*.5f,(minY+maxY-h)*.5f,w,h);
        }
        public void OnGUI()
        {
            if(Event.current.type!=EventType.Repaint) return;
            OpticsUpgrade sight=CurrentLockSight();
            if(sight==null) return;
            DrawFixedSightLines(sight);
            if(_lockStyle==null) _lockStyle=new GUIStyle(GUI.skin.label) {fontSize=14,wordWrap=false,clipping=TextClipping.Clip};
            Rect view=CameraManager.MainCam.pixelRect;view.y=Screen.height-view.yMax;
            float right=Mathf.Min(Screen.width,view.xMax),bottom=Mathf.Min(Screen.height,view.yMax);
            view.x=Mathf.Max(0f,view.x);view.y=Mathf.Max(0f,view.y);
            view.width=Mathf.Max(1f,right-view.x);view.height=Mathf.Max(1f,bottom-view.y);
            DrawSightScanPulse(sight,view);
            if(_targetLocks.Count>0 && sight==_lockOwner && !M8SightDamaged(sight))
            {
                Color old=GUI.color;DrawLockHud(view);GUI.color=old;
            }
            // Last overlay keeps brackets out of the opaque readout area.
            DrawTubeHud(sight,view); // Scope-local header and two six-cell pods.
        }
    }
}
