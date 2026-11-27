using System.Collections.Generic;
using AnimeCS.Content;
using StylizedCharacters;
using UnityEngine;

namespace AnimeCS.Play
{
    internal sealed class FirstPersonBody
    {
        private readonly FirstPersonVisibility _view = new();

        internal FirstPersonBody(GameObject root, OutfitDefinition outfit, List<Renderer> localWearables)
        {
            var regions = outfit ? outfit.Regions(false) : null;
            var preparedParts = new Dictionary<Renderer, Mesh>();
            foreach (var prepared in root.GetComponentsInChildren<PreparedCharacter>(true))
                foreach (var part in prepared.Body)
                    if (part.renderer && part.mesh) preparedParts.TryAdd(part.renderer, part.mesh);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (localWearables.Contains(renderer)) continue;
                byte[] mask = null;
                if (renderer is SkinnedMeshRenderer skin && regions != null)
                {
                    foreach (var region in regions)
                    {
                        if (skin.sharedMesh != region.partitioned) continue;
                        mask = region.firstPersonVisible;
                        break;
                    }
                }
                if (renderer is SkinnedMeshRenderer body)
                {
                    if (mask?.Length > 0) { _view.Split(body, mask); continue; }
                    if (preparedParts.TryGetValue(body, out var mesh)) { _view.Split(body, mesh); continue; }
                }
                _view.Conceal(renderer);
            }
        }

        internal void SetLocal(bool local) => _view.SetLocal(local);
        internal void SyncShadows() => _view.SyncShadows();
        internal void Release() => _view.Release();
    }
}
