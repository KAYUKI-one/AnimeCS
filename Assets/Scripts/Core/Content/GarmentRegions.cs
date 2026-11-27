using System;
using UnityEngine;

namespace AnimeCS.Content
{
    [Serializable]
    public sealed class GarmentRegions
    {
        public Mesh source, partitioned, firstPersonLower;
        public byte[] firstPersonVisible;
        public Vector3[] soleAnchors;
        public int[] lowerMaterialSlots;
        public Material[] materials;
        // -1 preserves face, hair and unassigned accessories. Other values are WearSlot indices.
        public int[] slots;
    }
}
