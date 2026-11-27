using System;
using AnimeCS.Content;
using UnityEngine;

namespace AnimeCS.Play
{
    internal static class WearablePresenter
    {
        internal static void Apply(ContentCatalog catalog,WornItems worn,OutfitDefinition outfit,bool view,GameObject body,CharacterRig rig,System.Collections.Generic.List<Renderer> localWearables=null)
        {
            if (!outfit || !body) return;
            var choices=new WearableDefinition[5];
            foreach(WearSlot slot in Enum.GetValues(typeof(WearSlot)))
            {
                var entry=catalog.Find<WearableDefinition>(worn.Get(slot));
                if(!entry || entry.Slot!=slot || !entry.Fits(outfit.Character))continue;
                choices[(int)slot]=entry;
                var model=entry.Model(view,outfit.Character);
                if(!model || view && slot==WearSlot.Headwear)continue;
                var anchor=slot==WearSlot.Headwear ? rig.HeadMount : body.transform.parent;
                if(anchor)
                {
                    var garment=AppearancePresenter.AddVisual(model,anchor,slot==WearSlot.Headwear?outfit.Character.HeadwearOffset:Vector3.zero);rig.BindClothing(garment);
                    if(localWearables!=null && (slot==WearSlot.Bottom || slot==WearSlot.Shoes || slot==WearSlot.Gloves))
                        localWearables.AddRange(garment.GetComponentsInChildren<Renderer>(true));
                    foreach(var anim in garment.GetComponentsInChildren<Animator>(true))anim.enabled=false;
                    foreach(var skin in garment.GetComponentsInChildren<SkinnedMeshRenderer>(true)){skin.updateWhenOffscreen=view;if(view)skin.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
                }
            }
            var regions=outfit.Regions(view);
            if(regions==null)return;
            foreach(var skin in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach(var region in regions)
            {
                if(region.source!=skin.sharedMesh || !region.partitioned)continue;
                var mats=(Material[])region.materials.Clone();
                for(int i=0;i<mats.Length;i++)
                {
                    int slot=region.slots[i];
                    if(slot<0 || !choices[slot])continue;
                    var choice=choices[slot];
                    if(choice.Model(view,outfit.Character) && outfit.HiddenRegion)mats[i]=outfit.HiddenRegion;
                    else if(choice.Material)mats[i]=choice.Material;
                }
                skin.sharedMesh=region.partitioned;skin.sharedMaterials=mats;break;
            }
        }
    }
}
