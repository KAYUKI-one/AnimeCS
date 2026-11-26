using System.Collections.Generic;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Play
{
    // Switch the existing animated skin's geometry. The feet keep the world depth, gait and equipped materials.
    internal sealed class FirstPersonBody
    {
        private sealed class Part
        {
            internal Renderer Renderer;
            internal SkinnedMeshRenderer Skin;
            internal bool WasVisible, LowerWearable;
            internal Mesh Full, Legs;
            internal Material[] WorldMaterials, LegMaterials;
        }
        private readonly List<Part> _parts = new();
        private bool _local;
        internal FirstPersonBody(GameObject root, OutfitDefinition outfit, List<Renderer> lowerWearables)
        {
            var regions=outfit ? outfit.Regions(false) : null;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var part=new Part{Renderer=renderer,WasVisible=renderer.enabled,LowerWearable=lowerWearables.Contains(renderer)};
                _parts.Add(part);
                if(renderer is not SkinnedMeshRenderer skin || regions==null) continue;
                foreach(var r in regions)
                {
                    if(!r.firstPersonLower || (skin.sharedMesh!=r.source && skin.sharedMesh!=r.partitioned))continue;
                    part.Skin=skin;part.Full=skin.sharedMesh;part.Legs=r.firstPersonLower;
                    part.WorldMaterials=skin.sharedMaterials;
                    var palette=part.Full==r.partitioned?part.WorldMaterials:r.materials;
                    part.LegMaterials=new Material[r.lowerMaterialSlots.Length];
                    for(int i=0;i<part.LegMaterials.Length;i++)part.LegMaterials[i]=palette[r.lowerMaterialSlots[i]];
                    break;
                }
            }
        }
        internal void SetLocal(bool local)
        {
            if(local==_local)return;
            _local=local;
            foreach(var p in _parts)
            {
                if(p.Skin){p.Skin.sharedMesh=local?p.Legs:p.Full;p.Skin.sharedMaterials=local?p.LegMaterials:p.WorldMaterials;}
                p.Renderer.enabled=p.WasVisible && (!local || p.Legs || p.LowerWearable);
            }
        }
    }
}
