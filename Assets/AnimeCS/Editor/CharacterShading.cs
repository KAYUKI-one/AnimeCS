using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Editor
{
    internal static class CharacterShading
    {
        // Atlas channels are authored differently per source. In particular several opaque colour maps use alpha for packing.
        internal static void Apply(string name, Material material)
        {
            if (!name.Contains("_")) return;
            string[] parts=name.Split('_'); string cast=parts[0], atlas=parts[1];
            bool face=cast switch {
                "Rover"=>atlas=="T02","Lily" or "Nora"=>atlas=="T01","Michele"=>atlas=="T02",_=>atlas=="T00" };
            bool hair=cast switch {
                "Rover"=>atlas=="T04", "Lily" or "Flor"=>atlas=="T05"||atlas=="T06",
                "Laevatain"=>atlas=="T05","Mornye"=>atlas=="T02"||atlas=="T03",
                "BlackMiku"=>atlas=="T02"||atlas=="T23","Michele"=>atlas=="T01","Nora"=>atlas=="T00",_=>false };
            bool metal=cast=="BlackMiku" && (atlas=="T11"||atlas=="BABABA"||atlas=="T26");
            bool satin=cast=="BlackMiku" && (atlas=="T04"||atlas=="T05"||atlas=="T16"||atlas=="T17");
            bool translucent=(cast=="Lily"&&atlas=="T07") || (cast=="BlackMiku"&&atlas=="T28") || (cast=="Mornye"&&atlas=="T08");
            bool clip=cast=="BlackMiku" && !translucent && atlas.StartsWith("T",StringComparison.Ordinal);
            var shader=Shader.Find("AnimeCS/Character Surface");
            if (!shader) throw new InvalidDataException("角色表面着色器未导入。");
            material.shader=shader;
            bool legShell=cast=="Mornye" && atlas=="T08";
            material.SetFloat("_Cull",legShell?(float)CullMode.Back:0);
            material.SetFloat("_AlphaToMask",clip?1:0);
            material.SetFloat("_ClearCoatMask",hair?.86f:0);
            material.SetFloat("_ClearCoatSmoothness",face?.87f:.12f);
            material.SetFloat("_Smoothness",metal?.77f:hair?.61f:satin?.53f:face?.38f:.26f);
            material.SetFloat("_Metallic",metal?.65f:0);
            material.SetColor("_SpecColor",Color.white*(metal?.45f:hair?.22f:satin?.18f:face?.071f:.1f));
            material.SetFloat("_Cutoff",clip?.24f:.5f);
            if (clip && material.GetTexture("_BaseMap") is Texture2D texture &&
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter albedo &&
                (!albedo.mipMapsPreserveCoverage || !Mathf.Approximately(albedo.alphaTestReferenceValue,.24f)))
            {
                albedo.mipMapsPreserveCoverage=true;albedo.alphaTestReferenceValue=.24f;
                albedo.SaveAndReimport();
            }
            material.SetFloat("_Surface",translucent?1:0);
            material.SetFloat("_ZWrite",translucent?0:1);
            material.SetFloat("_SrcBlend",(float)(translucent?BlendMode.SrcAlpha:BlendMode.One));
            material.SetFloat("_DstBlend",(float)(translucent?BlendMode.OneMinusSrcAlpha:BlendMode.Zero));
            CoreUtils.SetKeyword(material,"_SURFACE_TYPE_TRANSPARENT",translucent);
            CoreUtils.SetKeyword(material,"_ALPHATEST_ON",clip);
            material.DisableKeyword("_EMISSION");
            var tint=material.GetColor("_BaseColor");
            tint.a=legShell?.70f:1;
            material.SetColor("_BaseColor",tint);
            material.SetOverrideTag("RenderType",translucent?"Transparent":clip?"TransparentCutout":"Opaque");
            material.renderQueue=(int)(translucent?RenderQueue.Transparent:clip?RenderQueue.AlphaTest:RenderQueue.Geometry);
            material.SetShaderPassEnabled("ShadowCaster",!translucent);
            EditorUtility.SetDirty(material);
        }
    }
}
