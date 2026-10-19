using System;
using UnityEngine;

namespace M8LOSATM2
{
    internal static class M8MaterialRules
    {
        internal static Material SelectNativeM2(Material[] candidates)
        {
            if (candidates == null) return null;
            foreach (Material material in candidates)
            {
                if (material == null || material.shader == null ||
                    !material.name.StartsWith("M2BRADLEY", StringComparison.OrdinalIgnoreCase) ||
                    material.shader.name != "GHPC/VehicleShader") continue;
                if (material.HasProperty("_Albedo") && material.HasProperty("_Normal") &&
                    material.HasProperty("_Smoothness") && material.HasProperty("_Occlusion") &&
                    material.HasProperty("_CamoLayer") && material.HasProperty("_PaintMask") &&
                    material.HasProperty("_CamoAmount") && material.HasProperty("_Cutoff")) return material;
            }
            return null;
        }

        internal static void ApplyNativeBaseline(Material material, Texture2D albedo,
            Texture2D flatNormal, Texture2D matteSmoothness, bool alphaClip, float cutoff)
        {
            // Native M2 property names, verified against the 20261014 bundle.
            // Still a color/lighting isolation candidate, not final PBR restoration.
            material.SetTexture("_Albedo", albedo);
            material.SetTexture("_CamoLayer", albedo);
            material.SetTexture("_Normal", flatNormal);
            material.SetTexture("_Smoothness", matteSmoothness);
            material.SetTexture("_Occlusion", Texture2D.whiteTexture);
            material.SetTexture("_PaintMask", Texture2D.whiteTexture);
            foreach (string slot in new[] { "_Albedo", "_CamoLayer", "_Normal", "_Smoothness", "_Occlusion", "_PaintMask" })
            {
                material.SetTextureScale(slot, Vector2.one);
                material.SetTextureOffset(slot, Vector2.zero);
            }
            material.SetFloat("_CamoAmount", 0f);
            material.SetFloat("_ScorchAmount", 0f);
            material.SetFloat("_ScorchSmoothnessInfluence", 0f);
            material.SetFloat("_snow", 0f);
            material.SetFloat("_dirt", 0f);
            material.SetFloat("_hackcamoUVs", 0f);
            material.SetFloat("_Cutoff", alphaClip ? Mathf.Clamp01(cutoff) : 0f);
        }

        internal static void ApplyColorBaseline(Material material)
        {
            // Diagnostic baseline only: retain the source albedo/alpha, while
            // isolating imported tangent normals and metallic shader variants.
            // Do not brighten the atlas or turn the material into an emissive one.
            material.SetTexture("_BumpMap", null);
            material.SetTexture("_MetallicGlossMap", null);
            material.SetFloat("_BumpScale", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.35f);
            material.SetFloat("_GlossMapScale", 0f);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
        }

        internal static Color32 UncompressedNormal(Color32 value)
        {
            // Raw PNG normal maps are RGB, not DXT5. RGB + opaque alpha works
            // with both Unity's XYZ and RG/AG normal-unpacking shader variants.
            value.a = 255;
            return value;
        }
    }
}
