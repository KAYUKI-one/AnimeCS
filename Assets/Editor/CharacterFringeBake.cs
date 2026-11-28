using System.IO;
using DanbaidongRP.CharacterPipeline.Editor;
using UnityEditor;

namespace AnimeCS.Editor
{
    internal static class CharacterFringeBake
    {
        internal static void Bind(SerializedObject character, string modelPath)
        {
            string name = Path.GetFileNameWithoutExtension(modelPath);
            string explicitHair = name switch
            {
                "Firefly" => "Firefly_T02",
                "StarTraveler" => "StarTraveler_T12",
                "Lily" => "Lily_T05",
                _ => null
            };
            string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            if (explicitHair == null && !File.Exists($"{folder}/{name}Face.txt")) return;

            var (mesh, material) = DanbaidongCharacterSetupTool.BakeFringeMesh(
                modelPath, DanbaidongLookMaps.DefaultLookDevFolder, explicitHair);
            if (!mesh || !material) return;

            character.FindProperty("_fringeMesh").objectReferenceValue = mesh;
            character.FindProperty("_fringeMaterial").objectReferenceValue = material;
        }
    }
}
