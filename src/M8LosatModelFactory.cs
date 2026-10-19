using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using MelonLoader;

namespace M8LOSATM2
{
    internal sealed class M8ModelInstance : IDisposable
    {
        internal GameObject Root;
        internal M8TrackRig TrackRig;
        internal M8TubeVisuals TubeVisuals;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        internal void Own(UnityEngine.Object value) { _owned.Add(value); }

        internal Transform Find(string name)
        {
            if (Root == null) return null;
            foreach (Transform transform in Root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
            return null;
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
            foreach (UnityEngine.Object value in _owned)
                if (value != null) UnityEngine.Object.Destroy(value);
            _owned.Clear();
        }
    }

    internal static class M8LosatModelFactory
    {
        private static int Count(BinaryReader reader, int maximum)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum) throw new InvalidDataException("Invalid model count.");
            return value;
        }

        private static float Scalar(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Invalid model number.");
            return value;
        }

        private static Vector3 Vector3Value(BinaryReader reader)
        { return new Vector3(Scalar(reader), Scalar(reader), Scalar(reader)); }

        private static string AssetPath(string root, string relative)
        {
            if (Path.IsPathRooted(relative)) throw new InvalidDataException("Absolute texture path.");
            string value = Path.GetFullPath(Path.Combine(root, relative));
            if (!value.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Texture path escapes asset directory.");
            return value;
        }

        private static Texture2D LoadTexture(M8ModelInstance model, Dictionary<string, Texture2D> cache,
            string root, string relative, bool linear, bool normal)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            string key = relative + ":" + linear + ":" + normal;
            Texture2D cached;
            if (cache.TryGetValue(key, out cached)) return cached;
            string file = AssetPath(root, relative);
            if (!File.Exists(file) || new FileInfo(file).Length > 64000000)
                throw new InvalidDataException("Missing or oversized texture: " + relative);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            model.Own(texture);
            byte[] albedoBytes=!linear&&!normal?M8WeatheringTextures.ReadAlbedo(relative):null;
            if (!ImageConversion.LoadImage(texture, albedoBytes??File.ReadAllBytes(file), false))
                throw new InvalidDataException("Texture decode failed: " + relative);
            if (normal)
            {
                Color32[] pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = M8MaterialRules.UncompressedNormal(pixels[i]);
                }
                texture.SetPixels32(pixels);
            }
            else if(!linear&&M8AppearanceRules.PaintedAlbedo(relative))
            {
                var pixels=texture.GetPixels32();
                for(int i=0;i<pixels.Length;i++)pixels[i]=M8AppearanceRules.DarkerPaint(pixels[i]);
                texture.SetPixels32(pixels);
            }
            texture.name = Path.GetFileNameWithoutExtension(relative);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 4;
            texture.Apply(true, true);
            cache.Add(key, texture);
            return texture;
        }

        internal static M8ModelInstance Create(string assetDirectory, int layer)
        {
            string root = Path.GetFullPath(assetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var model = new M8ModelInstance { Root = new GameObject("M8_LOSAT_Runtime_Model") };
            model.Root.layer = layer;
            model.Root.SetActive(false);
            try
            {
                // This bounded material lookup runs only when a new M2 is converted,
                // never from UpdateBindings or a per-frame render callback.
                Material nativeTemplate = M8MaterialRules.SelectNativeM2(Resources.FindObjectsOfTypeAll<Material>());
                if (nativeTemplate == null)
                    throw new InvalidOperationException("Native M2 GHPC/VehicleShader material is unavailable.");
                using (var stream = File.OpenRead(Path.Combine(root, "GHPC", "M8_LOSAT.meshbin")))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != 800200 || reader.ReadInt32() != 2)
                        throw new InvalidDataException("Wrong M8 runtime format.");
                    int materialCount = Count(reader, 32);
                    int nodeCount = Count(reader, 128);
                    int lodCount = Count(reader, 4);
                    if (materialCount == 0 || nodeCount == 0 || lodCount != 3)
                        throw new InvalidDataException("Incomplete M8 runtime model.");

                    var materials = new Material[materialCount];
                    Material interiorMaterial = null;
                    var textureCache = new Dictionary<string, Texture2D>();
                    for (int index = 0; index < materialCount; index++)
                    {
                        string name = reader.ReadString();
                        Vector3 color = Vector3Value(reader);
                        float alpha = Scalar(reader), metallic = Scalar(reader), smoothness = Scalar(reader);
                        bool alphaClip = reader.ReadBoolean();
                        float cutoff = Scalar(reader);
                        Material material = new Material(nativeTemplate);
                        model.Own(material);
                        materials[index] = material;
                        material.name = name;
                        string basePath = reader.ReadString();
                        Texture2D baseColor = LoadTexture(model, textureCache, root, basePath, false, false);
                        // Data maps are linear. RGB normals with opaque alpha
                        // match the native compiled shader's R*A/G decoding.
                        Texture2D sourceNormal = LoadTexture(model, textureCache, root, reader.ReadString(), true, true);
                        Texture2D sourceFinish = LoadTexture(model, textureCache, root, reader.ReadString(), true, false);
                        if (baseColor == null || sourceNormal == null || sourceFinish == null)
                            throw new InvalidDataException("M8 surface maps are missing.");
                        M8MaterialRules.ApplyNativeBaseline(material, baseColor, sourceNormal, sourceFinish, alphaClip, cutoff);
                        if(index==2)interiorMaterial=M8LauncherInterior.MakeMaterial(model,material,
                            M8WeatheringTextures.ReadAlbedo(basePath)??File.ReadAllBytes(AssetPath(root,basePath)));
                    }

                    var nodes = new Transform[nodeCount];
                    for (int index = 0; index < nodeCount; index++)
                    {
                        string name = reader.ReadString();
                        int parent = reader.ReadInt32();
                        if (parent < -1 || parent >= index) throw new InvalidDataException("Invalid node hierarchy.");
                        GameObject node = new GameObject(name);
                        node.layer = layer;
                        nodes[index] = node.transform;
                        node.transform.SetParent(parent < 0 ? model.Root.transform : nodes[parent], false);
                        node.transform.localPosition = Vector3Value(reader);
                        Quaternion rotation = new Quaternion(Scalar(reader), Scalar(reader), Scalar(reader), Scalar(reader));
                        float length = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y +
                                                  rotation.z * rotation.z + rotation.w * rotation.w);
                        if (Mathf.Abs(length - 1f) > 0.001f) throw new InvalidDataException("Invalid node rotation.");
                        node.transform.localRotation = rotation;
                        node.transform.localScale = Vector3Value(reader);
                    }

                    model.TrackRig = new M8TrackRig(model, root);
                    model.TubeVisuals = new M8TubeVisuals(root);
                    var lods = new LOD[lodCount];
                    int totalVertices = 0;
                    for (int level = 0; level < lodCount; level++)
                    {
                        if (reader.ReadInt32() != level) throw new InvalidDataException("Invalid LOD order.");
                        float height = Scalar(reader);
                        int rendererCount = Count(reader, 64);
                        var renderers = new List<Renderer>(rendererCount+12);
                        for (int item = 0; item < rendererCount; item++)
                        {
                            int nodeIndex = Count(reader, nodeCount - 1);
                            int vertexCount = Count(reader, 250000);
                            int submeshCount = Count(reader, 16);
                            totalVertices += vertexCount;
                            if (vertexCount < 3 || submeshCount == 0 || totalVertices > 1200000)
                                throw new InvalidDataException("M8 model budget exceeded.");
                            var positions = new Vector3[vertexCount];
                            var normals = new Vector3[vertexCount];
                            var uvs = new Vector2[vertexCount];
                            for (int vertex = 0; vertex < vertexCount; vertex++)
                            {
                                positions[vertex] = Vector3Value(reader);
                                normals[vertex] = Vector3Value(reader);
                                uvs[vertex] = new Vector2(Scalar(reader), Scalar(reader));
                            }
                            Mesh mesh = new Mesh();
                            model.Own(mesh);
                            mesh.name = nodes[nodeIndex].name + "_LOD" + level;
                            mesh.indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                            mesh.vertices = positions;
                            mesh.normals = normals;
                            mesh.uv = uvs;
                            mesh.subMeshCount = submeshCount;
                            var assigned = new Material[submeshCount];
                            for (int submesh = 0; submesh < submeshCount; submesh++)
                            {
                                int materialIndex = Count(reader, materialCount - 1);
                                int indexCount = Count(reader, 700000);
                                if (indexCount % 3 != 0) throw new InvalidDataException("Incomplete triangle list.");
                                var indices = new int[indexCount];
                                for (int triangleIndex = 0; triangleIndex < indexCount; triangleIndex++)
                                    indices[triangleIndex] = Count(reader, vertexCount - 1);
                                mesh.SetTriangles(indices, submesh);
                                assigned[submesh] = materials[materialIndex];
                            }
                            if(nodes[nodeIndex].name=="M8_Turret_Visual")M8LauncherPartitions.FlattenRoofLip(mesh);
                            mesh.RecalculateTangents();
                            mesh.RecalculateBounds();
                            GameObject rendererObject = new GameObject("LOD" + level);
                            rendererObject.layer = layer;
                            rendererObject.transform.SetParent(nodes[nodeIndex], false);
                            if(nodes[nodeIndex].name=="M8_Turret_Visual")
                            {
                                renderers.AddRange(model.TubeVisuals.Split(model,mesh,assigned,rendererObject,level));
                                renderers.Add(M8LauncherInterior.SplitFrame(model,mesh,interiorMaterial,rendererObject,level));
                                renderers.Add(M8LauncherPartitions.Create(model,materials[2],rendererObject,level));
                            }
                            Renderer renderer;
                            if(nodes[nodeIndex].name == "M8_Tracks_Static_Visual")
                                renderer = model.TrackRig.MakeRenderer(rendererObject, mesh, assigned, level);
                            else
                            {
                                rendererObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                                renderer = rendererObject.AddComponent<MeshRenderer>();
                                renderer.sharedMaterials = assigned;
                            }
                            renderer.shadowCastingMode = ShadowCastingMode.On;
                            renderer.receiveShadows = true;
                            renderers.Add(renderer);
                        }
                        lods[level] = new LOD(height, renderers.ToArray());
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("Trailing M8 runtime data.");
                    LODGroup group = model.Root.AddComponent<LODGroup>();
                    group.SetLODs(lods);
                    group.RecalculateBounds();
                }
                model.Root.SetActive(true);
                return model;
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }
    }
}
