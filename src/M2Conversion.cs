using System;
using System.Collections.Generic;
using GHPC.Vehicle;
using MelonLoader;
using NWH.WheelController3D;
using UnityEngine;

namespace M8LOSATM2
{
    internal sealed class M2Conversion : IDisposable
    {
        private readonly Vehicle _vehicle;
        private readonly M8ModelInstance _model;
        private readonly Renderer[] _nativeRenderers;
        private readonly bool[] _nativeRendererEnabled;
        private readonly bool[] _nativeForceOff;
        private readonly Transform _nativeTurret;
        private readonly Transform _modelTurret;
        private readonly WheelController[] _controllers;
        private readonly Transform[] _wheelNodes;
        private M8WeaponBinding _weapons;
        private M8InteriorRig _interior;
        private M8BalanceTuning _balance;
        private M8FittedArmorRig _fittedArmor;
        private M8PartitionArmor _partitionArmor;
        private M8VehicleName _displayName;
        private M8ViewpointRig _viewpoints;
        private readonly float[] _wheelAngles=new float[12];
        private int _motionFrame=-1;
        private bool _disposed;

        internal bool IsAlive
        {
            get { return _vehicle != null && _vehicle.gameObject != null && _vehicle.gameObject.scene.IsValid(); }
        }

        private M2Conversion(Vehicle vehicle, M8ModelInstance model, Renderer[] nativeRenderers,
            bool[] nativeRendererEnabled,
            Transform nativeTurret, Transform modelTurret, WheelController[] controllers,
            Transform[] wheelNodes)
        {
            _vehicle = vehicle;
            _model = model;
            _nativeRenderers = nativeRenderers;
            _nativeRendererEnabled = nativeRendererEnabled;
            _nativeForceOff=new bool[nativeRenderers.Length];
            for(int i=0;i<nativeRenderers.Length;i++)if(nativeRenderers[i]!=null)
            {_nativeForceOff[i]=nativeRenderers[i].forceRenderingOff;nativeRenderers[i].forceRenderingOff=true;}
            _nativeTurret = nativeTurret;
            _modelTurret = modelTurret;
            _controllers = controllers;
            _wheelNodes = wheelNodes;
        }

        internal static bool IsReady(Vehicle vehicle, out string reason)
        {
            reason = string.Empty;
            if (vehicle == null || vehicle.gameObject == null)
            {
                reason = "Vehicle is unavailable.";
                return false;
            }
            if (FindNativeTurret(vehicle.transform) == null)
            {
                reason = "Native M2 turret binding is not ready.";
                return false;
            }
            if (FindWheels(vehicle) == null)
            {
                reason = "Native M2 wheel bindings are not ready.";
                return false;
            }
            if(M8WeaponBinding.FindLauncher(vehicle)==null){reason="Native M2 TOW/FCS is not ready.";return false;}
            if(!M8BalanceTuning.IsReady(vehicle,out reason))return false;
            if(!M8FittedArmorRig.IsReady(vehicle,out reason))return false;
            return M8InteriorRig.IsReady(vehicle,out reason);
        }

        internal static bool TryCreate(Vehicle vehicle, string assetDirectory, LosatCore.LosatService service, out M2Conversion result)
        {
            result = null;
            M8ModelInstance model = null;
            Renderer[] snapshot = null;
            bool[] enabledSnapshot = null;
            try
            {
                Transform nativeTurret = FindNativeTurret(vehicle.transform);
                if (nativeTurret == null) return false;
                WheelController[] controllers = FindWheels(vehicle);
                if (controllers == null) return false;
                var nativeMeshes = new List<Renderer>();
                foreach (Renderer renderer in vehicle.GetComponentsInChildren<Renderer>(true))
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                        nativeMeshes.Add(renderer);
                snapshot = nativeMeshes.ToArray();
                enabledSnapshot = new bool[snapshot.Length];
                for (int index = 0; index < snapshot.Length; index++)
                    enabledSnapshot[index] = snapshot[index].enabled;
                model = M8LosatModelFactory.Create(assetDirectory, vehicle.gameObject.layer);
                model.Root.transform.SetParent(vehicle.transform, false);
                model.Root.transform.localPosition = Vector3.zero;
                model.Root.transform.localRotation = Quaternion.identity;
                model.Root.transform.localScale = Vector3.one;
                Transform turret = model.Find("Turret_PIVOT");
                if (turret == null) throw new InvalidOperationException("M8 turret binding node is missing.");
                var wheelNodes = new Transform[12];
                for (int axle = 1; axle <= 6; axle++)
                {
                    wheelNodes[(axle - 1) * 2] = model.Find("Wheel_L" + axle + "_PIVOT");
                    wheelNodes[(axle - 1) * 2 + 1] = model.Find("Wheel_R" + axle + "_PIVOT");
                    if (wheelNodes[(axle - 1) * 2] == null || wheelNodes[(axle - 1) * 2 + 1] == null)
                        throw new InvalidOperationException("M8 wheel binding node is missing.");
                }

                foreach (Renderer renderer in snapshot)
                    if (renderer != null) renderer.enabled = false;
                model.Root.SetActive(true);
                result = new M2Conversion(vehicle, model, snapshot, enabledSnapshot, nativeTurret, turret,
                    controllers, wheelNodes);
                result.UpdateBindings();
                result._weapons=M8WeaponBinding.Create(vehicle,turret,service,result.SynchronizeTurret,result.HideTubeContents);
                result._interior=M8InteriorRig.Apply(vehicle,turret,index=>service.MarkTubeLost(M8WeaponBinding.FindLauncher(vehicle),index));
                result._weapons.BindMissileDamage(result._interior.MissileDamage);
                result._fittedArmor=M8FittedArmorRig.Apply(vehicle,turret);
                result._balance=M8BalanceTuning.Apply(vehicle);
                result._partitionArmor=M8PartitionArmor.Apply(vehicle,turret);
                result._displayName=new M8VehicleName(vehicle.BaseUnit);
                try{result._viewpoints=M8ViewpointRig.Apply(vehicle,turret,nativeTurret,M8WeaponBinding.FindLauncher(vehicle).FCS);}
                catch(Exception viewError){MelonLogger.Warning(Mod.Prefix+"Viewpoint positioning unavailable; M8 conversion retained: "+viewError.Message);}
                return true;
            }
            catch (Exception exception)
            {
                if(result!=null){result.Dispose();result=null;model=null;}
                if (snapshot != null)
                    for (int index = 0; index < snapshot.Length; index++)
                        if (snapshot[index] != null) snapshot[index].enabled = enabledSnapshot[index];
                if (model != null) model.Dispose();
                MelonLogger.Error(Mod.Prefix + "conversion rolled back safely: " + exception);
                return false;
            }
        }

        internal void UpdateBindings()
        {
            if (!IsAlive || _model.Root == null) return;
            SynchronizeTurret();
            if(_viewpoints!=null)_viewpoints.Maintain();
            // Native LOD/weapon visibility can re-enable an old gun renderer.
            // Force it off without deactivating shared FCS, camera or mount nodes.
            foreach(var renderer in _nativeRenderers)if(renderer!=null&&!renderer.forceRenderingOff)renderer.forceRenderingOff=true;
            if(_weapons!=null)_weapons.MaintainSelection();
            if(_interior!=null)_interior.Maintain();
            if(_motionFrame==Time.frameCount)return;
            _motionFrame=Time.frameCount;
            float left=0f,right=0f;int lc=0,rc=0;
            for (int index = 0; index < _controllers.Length && index < _wheelNodes.Length; index++)
            {
                WheelController controller = _controllers[index];
                Transform node = _wheelNodes[index];
                if (controller == null || node == null) continue;
                // NWH intentionally never updates visRotAngle for tracked vehicles.
                // Read physics angular velocity, retaining slip, reverse and pivot turns.
                if(controller.wheel==null)continue;
                float step=M8WheelMotion.Distance(controller.wheel.angularVelocity,controller.Radius,Time.deltaTime);
                _wheelAngles[index]=M8WheelMotion.AdvanceAngle(_wheelAngles[index],step);
                node.localRotation = Quaternion.Euler(_wheelAngles[index], 0f, 0f);
                if(index%2==0){left+=step;lc++;}else{right+=step;rc++;}
            }
            if(lc>0&&rc>0&&_model.TrackRig!=null)_model.TrackRig.Advance(left/lc,right/rc);
        }

        private void HideTubeContents(int index)
        {if(_model.TubeVisuals!=null)_model.TubeVisuals.Fired(index);if(_interior!=null)_interior.Fired(index);}

        private void SynchronizeTurret()
        {
            if (_nativeTurret != null && _modelTurret != null)
            {
                Quaternion relative = Quaternion.Inverse(_vehicle.transform.rotation) * _nativeTurret.rotation;
                _modelTurret.localRotation = relative;
            }
        }

        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            if(_viewpoints!=null)_viewpoints.Dispose();
            if(_displayName!=null)_displayName.Dispose();
            if(_partitionArmor!=null)_partitionArmor.Dispose();
            if(_balance!=null)_balance.Dispose();
            if(_fittedArmor!=null)_fittedArmor.Dispose();
            if(_weapons!=null)_weapons.Dispose();
            if(_interior!=null)_interior.Dispose();
            if (_nativeRenderers != null)
                for (int index = 0; index < _nativeRenderers.Length; index++)
                    if (_nativeRenderers[index] != null)
                    {
                        _nativeRenderers[index].enabled = _nativeRendererEnabled[index];
                        _nativeRenderers[index].forceRenderingOff=_nativeForceOff[index];
                    }
            if (_model != null) _model.Dispose();
        }

        private static Transform FindTransform(Transform root, string name)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase)) return transform;
            return null;
        }

        private static Transform FindNativeTurret(Transform root)
        {
            if (root == null) return null;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(transform.name, "Turret", StringComparison.OrdinalIgnoreCase)) continue;
                string path = HierarchyPath(transform);
                if (path.EndsWith("M2BRADLEY_rig/HULL/Turret", StringComparison.OrdinalIgnoreCase))
                    return transform;
            }
            return null;
        }

        private static string HierarchyPath(Transform transform)
        {
            var names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static WheelController[] FindWheels(Vehicle vehicle)
        {
            WheelController[] all = vehicle.GetComponentsInChildren<WheelController>(true);
            if (all == null || all.Length < 12) return null;
            var list = new List<WheelController>(all);
            list.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            var ordered = new WheelController[12];
            for (int axle = 1; axle <= 6; axle++)
            {
                ordered[(axle - 1) * 2] = list.Find(item => item.name.IndexOf("L" + axle,
                    StringComparison.OrdinalIgnoreCase) >= 0);
                ordered[(axle - 1) * 2 + 1] = list.Find(item => item.name.IndexOf("R" + axle,
                    StringComparison.OrdinalIgnoreCase) >= 0);
            }
            foreach (WheelController controller in ordered) if (controller == null) return null;
            return ordered;
        }
    }
}
