using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.PhysicsHelpers;
using GHPC.Vehicle;
using UnityEngine;

namespace M8LOSATM2
{
    internal sealed class M8DamageRig : IDisposable
    {
        private sealed class ArmorState
        {
            internal UniformArmor Armor;
            internal bool ArmorEnabled;
            internal Collider Collider;
            internal bool ColliderEnabled;
            internal Mesh OriginalMesh;
        }

        private sealed class TransformState
        {
            internal Transform Transform;
            internal Transform Parent;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Scale;
        }

        private readonly List<ArmorState> _states = new List<ArmorState>();
        private readonly List<TransformState> _moved = new List<TransformState>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private SphereCollider _bound;
        private float _originalRadius;

        private struct Box
        {
            internal Vector3 Center;
            internal Vector3 Size;
            internal Box(Vector3 center, Vector3 size) { Center = center; Size = size; }
        }

        private M8DamageRig() { }

        internal static bool IsReady(Vehicle vehicle, out string reason)
        {
            reason = string.Empty;
            if (vehicle == null)
            {
                reason = "Vehicle is unavailable.";
                return false;
            }

            try
            {
                UniformArmor[] armors = M8ArmorDiscovery.FindOwned(vehicle);
                if (armors == null || armors.Length < 20)
                {
                    reason = "Stock M2 owned armor rig is not ready (" + armors.Length + " components).";
                    return false;
                }

                string[] exterior = {
                    "Hull Front Alu 5083", "Hull rear Allu 5083", "Hull Sides Alu 7039",
                    "Hull roof Alu 5083", "Hull Bottoms Alu 5083", "Turret Front Alu 7039",
                    "Turret Rear Alu", "Turret Sides Alu", "Turret Roof Alu", "Turret Bottom Alu",
                };
                foreach (string token in exterior)
                {
                    UniformArmor armor = FindArmor(armors, token, true);
                    if (armor.GetComponent<MeshCollider>() == null)
                    {
                        reason = "M2 plate has no mesh collider: " + token;
                        return false;
                    }
                }

                FindArmor(armors, "Engine_Cummings-VT903", false);
                FindArmor(armors, "AmmoRack_TowRack_001", false);
                FindArmor(armors, "AmmoRack_TowRack_002", false);
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        internal static M8DamageRig Apply(Vehicle vehicle)
        {
            var rig = new M8DamageRig();
            try
            {
                UniformArmor[] armors = M8ArmorDiscovery.FindOwned(vehicle);
                if (armors == null || armors.Length < 20)
                    throw new InvalidOperationException("Stock M2 armor rig is not ready.");
                foreach (UniformArmor armor in armors) rig.Remember(armor);

                rig.ApplyPlate(vehicle, armors, "Hull Front Alu 5083", new Box(
                    new Vector3(0f, 1.05f, 3.08f), new Vector3(2.70f, 1.35f, 0.08f)));
                rig.ApplyPlate(vehicle, armors, "Hull rear Allu 5083", new Box(
                    new Vector3(0f, 1.05f, -3.08f), new Vector3(2.70f, 1.35f, 0.08f)));
                rig.ApplyPlate(vehicle, armors, "Hull Sides Alu 7039",
                    new Box(new Vector3(-1.36f, 1.05f, 0f), new Vector3(0.08f, 1.35f, 6.08f)),
                    new Box(new Vector3(1.36f, 1.05f, 0f), new Vector3(0.08f, 1.35f, 6.08f)));
                rig.ApplyPlate(vehicle, armors, "Hull roof Alu 5083", new Box(
                    new Vector3(0f, 1.76f, 0f), new Vector3(2.70f, 0.08f, 6.00f)));
                rig.ApplyPlate(vehicle, armors, "Hull Bottoms Alu 5083", new Box(
                    new Vector3(0f, 0.38f, 0f), new Vector3(2.62f, 0.08f, 5.95f)));

                rig.ApplyPlate(vehicle, armors, "Turret Front Alu 7039", new Box(
                    new Vector3(0f, 2.65f, 1.93f), new Vector3(2.62f, 2.18f, 0.08f)));
                rig.ApplyPlate(vehicle, armors, "Turret Rear Alu", new Box(
                    new Vector3(0f, 2.65f, -1.51f), new Vector3(2.62f, 2.18f, 0.08f)));
                rig.ApplyPlate(vehicle, armors, "Turret Sides Alu", new Box(
                    new Vector3(-1.32f, 2.65f, 0.20f), new Vector3(0.08f, 2.18f, 3.36f)),
                    new Box(new Vector3(1.32f, 2.65f, 0.20f), new Vector3(0.08f, 2.18f, 3.36f)));
                rig.ApplyPlate(vehicle, armors, "Turret Roof Alu", new Box(
                    new Vector3(0f, 3.78f, 0.20f), new Vector3(2.60f, 0.08f, 3.34f)));
                rig.ApplyPlate(vehicle, armors, "Turret Bottom Alu", new Box(
                    new Vector3(0f, 1.57f, 0.20f), new Vector3(2.60f, 0.08f, 3.34f)));

                rig.RemoveM2HitGeometry(vehicle, armors);
                rig.PlaceRetainedInternals(vehicle, armors);
                rig.ExpandRaycastBound(vehicle);
                Physics.SyncTransforms();
                return rig;
            }
            catch
            {
                rig.Dispose();
                throw;
            }
        }

        private void Remember(UniformArmor armor)
        {
            Collider collider = armor == null ? null : armor.GetComponent<Collider>();
            _states.Add(new ArmorState
            {
                Armor = armor,
                ArmorEnabled = armor != null && armor.enabled,
                Collider = collider,
                ColliderEnabled = collider != null && collider.enabled,
                OriginalMesh = collider is MeshCollider ? ((MeshCollider)collider).sharedMesh : null,
            });
        }

        private void ApplyPlate(Vehicle vehicle, UniformArmor[] armors, string token, params Box[] boxes)
        {
            UniformArmor armor = FindArmor(armors, token, true);
            MeshCollider collider = armor.GetComponent<MeshCollider>();
            if (collider == null) throw new InvalidOperationException("M2 plate has no mesh collider: " + token);
            Mesh mesh = BuildBoxes(vehicle.transform, armor.transform, token, boxes);
            _meshes.Add(mesh);
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            collider.enabled = true;
            armor.enabled = true;
            armor.gameObject.layer = GHPC.Utility.CodeUtils.LAYER_INDEX_RAYCAST_ONLY;
        }

        private static UniformArmor FindArmor(UniformArmor[] armors, string token, bool exterior)
        {
            UniformArmor found = null;
            foreach (UniformArmor armor in armors)
            {
                if (armor == null) continue;
                string path = Path(armor.transform);
                if (exterior && path.IndexOf("bradley_armour_rigged", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (path.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (found != null) throw new InvalidOperationException("Ambiguous M2 armor plate: " + token);
                found = armor;
            }
            if (found == null) throw new InvalidOperationException("Missing M2 armor plate: " + token);
            return found;
        }

        private void RemoveM2HitGeometry(Vehicle vehicle, UniformArmor[] armors)
        {
            foreach (UniformArmor armor in armors)
            {
                if (armor == null) continue;
                string path = Path(armor.transform);
                bool exterior = path.IndexOf("bradley_armour_rigged", StringComparison.OrdinalIgnoreCase) >= 0;
                bool interior = path.IndexOf("bradley_int_rigged", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!exterior && !interior) continue; // Retain native crew hit zones.
                MeshCollider meshCollider = armor.GetComponent<MeshCollider>();
                bool replacement = meshCollider != null && _meshes.Contains(meshCollider.sharedMesh);
                bool retainedInternal = IsRetainedInternal(path);
                if (replacement || retainedInternal) continue;
                armor.enabled = false;
                Collider collider = armor.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
            }
        }

        private void PlaceRetainedInternals(Vehicle vehicle, UniformArmor[] armors)
        {
            PlaceInternal(vehicle, armors, "Engine_Cummings-VT903", new Vector3(0f, 0.85f, -1.65f));
            PlaceInternal(vehicle, armors, "AmmoRack_TowRack_001", new Vector3(-0.72f, 2.30f, 0.15f));
            PlaceInternal(vehicle, armors, "AmmoRack_TowRack_002", new Vector3(0.72f, 2.30f, 0.15f));
        }

        private void PlaceInternal(Vehicle vehicle, UniformArmor[] armors, string token, Vector3 position)
        {
            UniformArmor armor = FindArmor(armors, token, false);
            Transform transform = armor.transform;
            _moved.Add(new TransformState
            {
                Transform = transform,
                Parent = transform.parent,
                Position = transform.localPosition,
                Rotation = transform.localRotation,
                Scale = transform.localScale,
            });
            transform.SetParent(vehicle.transform, false);
            transform.localPosition = position;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            armor.enabled = true;
            Collider collider = armor.GetComponent<Collider>();
            if (collider != null) collider.enabled = true;
        }

        private static bool IsRetainedInternal(string path)
        {
            return path.IndexOf("Engine_Cummings-VT903", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("AmmoRack_TowRack_001", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("AmmoRack_TowRack_002", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ExpandRaycastBound(Vehicle vehicle)
        {
            RaycastColliderGroup group = vehicle.GetComponent<RaycastColliderGroup>() ??
                                         vehicle.GetComponentInChildren<RaycastColliderGroup>(true);
            if (group == null) return;
            FieldInfo field = typeof(RaycastColliderGroup).GetField("boundingCollider",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _bound = field == null ? null : field.GetValue(group) as SphereCollider;
            if (_bound == null) return;
            _originalRadius = _bound.radius;
            _bound.radius = Mathf.Max(_bound.radius, 5.1f);
        }

        private Mesh BuildBoxes(Transform vehicle, Transform armor, string name, Box[] boxes)
        {
            Matrix4x4 conversion = armor.worldToLocalMatrix * vehicle.localToWorldMatrix;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Box box in boxes) AddBox(vertices, triangles, conversion, box.Center, box.Size);
            Mesh mesh = new Mesh();
            mesh.name = "M8_LOSAT_" + name.Replace(' ', '_');
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddBox(List<Vector3> vertices, List<int> triangles, Matrix4x4 conversion,
            Vector3 center, Vector3 size)
        {
            int start = vertices.Count;
            Vector3 half = size * 0.5f;
            for (int z = -1; z <= 1; z += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int x = -1; x <= 1; x += 2)
                        vertices.Add(conversion.MultiplyPoint3x4(center + Vector3.Scale(half,
                            new Vector3(x, y, z))));
            int[] indices = {
                0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
                0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3,
                0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6,
            };
            foreach (int index in indices) triangles.Add(start + index);
        }

        private static string Path(Transform transform)
        {
            string value = transform == null ? string.Empty : transform.name;
            while (transform != null && transform.parent != null)
            {
                transform = transform.parent;
                value = transform.name + "/" + value;
            }
            return value;
        }

        public void Dispose()
        {
            for (int index = _moved.Count - 1; index >= 0; index--)
            {
                TransformState state = _moved[index];
                if (state.Transform == null) continue;
                state.Transform.SetParent(state.Parent, false);
                state.Transform.localPosition = state.Position;
                state.Transform.localRotation = state.Rotation;
                state.Transform.localScale = state.Scale;
            }
            foreach (ArmorState state in _states)
            {
                if (state.Armor != null) state.Armor.enabled = state.ArmorEnabled;
                if (state.Collider != null)
                {
                    MeshCollider meshCollider = state.Collider as MeshCollider;
                    if (meshCollider != null) meshCollider.sharedMesh = state.OriginalMesh;
                    state.Collider.enabled = state.ColliderEnabled;
                }
            }
            if (_bound != null) _bound.radius = _originalRadius;
            foreach (Mesh mesh in _meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            _meshes.Clear();
            _states.Clear();
            _moved.Clear();
        }
    }
}
