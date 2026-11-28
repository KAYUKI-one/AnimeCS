using DanbaidongRP.CharacterPipeline.Editor;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal static class CharacterLookMaps
    {
        internal static Texture2D Ramp(DanbaidongSurfaceRole role, bool forceRegenerate = false) =>
            DanbaidongLookMaps.GetOrCreateRamp(role, DanbaidongLookMaps.DefaultLookDevFolder, forceRegenerate);

        internal static Texture2D HairSpec(bool forceRegenerate = false) =>
            DanbaidongLookMaps.GetOrCreateHairSpecTexture(DanbaidongLookMaps.DefaultLookDevFolder, forceRegenerate);

        internal static Texture2D MaterialMask(Material material, DanbaidongSurfaceRole role, bool forceRegenerate = false) =>
            DanbaidongLookMaps.GetOrCreatePBRMask(material, role, forceRegenerate);
    }
}
