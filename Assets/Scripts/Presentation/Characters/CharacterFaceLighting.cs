using System.Collections.Generic;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Play
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1200)]
    internal sealed class CharacterFaceLighting : MonoBehaviour
    {
        private static readonly int FaceMap = Shader.PropertyToID("_FaceLightMap");
        private static readonly int Front = Shader.PropertyToID("_FaceFrontDirWS");
        private static readonly int Right = Shader.PropertyToID("_FaceRightDirWS");
        private static readonly int HeadCenter = Shader.PropertyToID("_HeadCenterWS");
        private static readonly int HeadMatrix = Shader.PropertyToID("_WorldToHeadMatrix");
        private MaterialPropertyBlock _properties;
        private Transform _basis;
        private List<(Renderer renderer, int slot)> _faces;

        internal static void Bind(GameObject body, Transform basis, CharacterDefinition definition)
        {
            if (definition && definition.FringeMesh && definition.FringeMaterial)
            {
                var source = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var authored = definition.Model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (source && authored && source.sharedMesh == authored.sharedMesh)
                {
                    var fringe = new GameObject("Fringe shadow").AddComponent<SkinnedMeshRenderer>();
                    fringe.transform.SetParent(source.transform, false);
                    fringe.sharedMesh = definition.FringeMesh;
                    fringe.sharedMaterial = definition.FringeMaterial;
                    fringe.bones = source.bones;
                    fringe.rootBone = source.rootBone;
                    fringe.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    fringe.receiveShadows = false;
                }
            }
            List<(Renderer, int)> faces = null;
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                    if (materials[slot] && (materials[slot].HasProperty(FaceMap) || materials[slot].HasProperty(HeadCenter)))
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
                _properties.SetVector(HeadCenter, _basis.position);
                _properties.SetMatrix(HeadMatrix, _basis.worldToLocalMatrix);
                renderer.SetPropertyBlock(_properties, slot);
            }
        }
    }
}
