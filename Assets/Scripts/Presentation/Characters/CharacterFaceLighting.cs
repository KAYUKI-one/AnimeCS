using System.Collections.Generic;
using UnityEngine;

namespace AnimeCS.Play
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1200)]
    internal sealed class CharacterFaceLighting : MonoBehaviour
    {
        private static readonly int FaceMap = Shader.PropertyToID("_FaceLightMap");
        private static readonly int Front = Shader.PropertyToID("_FaceFrontDirWS");
        private static readonly int Right = Shader.PropertyToID("_FaceRightDirWS");
        private MaterialPropertyBlock _properties;
        private Transform _basis;
        private List<(Renderer renderer, int slot)> _faces;

        internal static void Bind(GameObject body, Transform basis)
        {
            List<(Renderer, int)> faces = null;
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                    if (materials[slot] && materials[slot].HasProperty(FaceMap))
                        (faces ??= new()).Add((renderer, slot));
            }
            if (faces == null) return;
            if (!body.TryGetComponent<CharacterFaceLighting>(out var lighting))
                lighting = body.AddComponent<CharacterFaceLighting>();
            lighting._properties ??= new MaterialPropertyBlock();
            lighting._basis = basis;
            lighting._faces = faces;
            lighting.LateUpdate();
        }

        private void LateUpdate()
        {
            if (!_basis) return;
            foreach (var (renderer, slot) in _faces)
            {
                if (!renderer) continue;
                renderer.GetPropertyBlock(_properties, slot);
                _properties.SetVector(Front, _basis.forward);
                _properties.SetVector(Right, _basis.right);
                renderer.SetPropertyBlock(_properties, slot);
            }
        }
    }
}
