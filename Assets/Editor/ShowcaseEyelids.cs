using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal static class ShowcaseEyelids
    {
        // Model-space metres. The eye material supplies the vertical aperture, while
        // these extents include the surrounding socket and exclude the bridge of the nose.
        internal static void Bake(SkinnedMeshRenderer renderer, Transform root)
        {
            var mesh = renderer.sharedMesh;
            string eye, face;
            float inner, outer;
            switch (renderer.name)
            {
                case "LilyBody": eye = "Lily_T00"; face = "Lily_T01"; inner = .011f; outer = .073f; break;
                case "FireflyBody": eye = "Firefly_Eye"; face = "Firefly_T01"; inner = .010f; outer = .071f; break;
                case "MishtyBody": eye = "Mishty_Eye_01"; face = "Mishty_Face_00"; inner = .009f; outer = .069f; break;
                default: return;
            }
            var slots = renderer.sharedMaterials;
            int eyeSlot = Array.FindIndex(slots, m => m && m.name == eye);
            if (eyeSlot < 0 || mesh.GetBlendShapeIndex("ShowcaseBlink") >= 0) return;
            Matrix4x4 toModel = root.worldToLocalMatrix * renderer.localToWorldMatrix;
            var vertices = mesh.vertices;
            var aperture = new Bounds(toModel.MultiplyPoint3x4(vertices[mesh.GetIndices(eyeSlot)[0]]), Vector3.zero);
            foreach (int i in mesh.GetIndices(eyeSlot)) aperture.Encapsulate(toModel.MultiplyPoint3x4(vertices[i]));
            var movable = new HashSet<int>();
            for (int i = 0; i < slots.Length; i++)
            {
                string name = slots[i] ? slots[i].name : "";
                if (name == face || name == eye || name.Contains("Eye") || name.Contains("Lash"))
                    movable.UnionWith(mesh.GetIndices(i));
            }
            float low = aperture.min.y, high = aperture.max.y;
            float height = high - low;
            if (height <= 0f) return;
            var deltas = new Vector3[vertices.Length];
            Vector3 up = toModel.inverse.MultiplyVector(Vector3.up);
            foreach (int i in movable)
            {
                Vector3 v = toModel.MultiplyPoint3x4(vertices[i]);
                float x = Mathf.Abs(v.x);
                float lateral = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner - .008f, inner + .003f, x)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outer + .009f, outer - .003f, x));
                float vertical = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(low - height * .6f, low, v.y)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(high + height * .72f, high + height * .06f, v.y));
                float depth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.045f, .012f, Mathf.Abs(v.z - aperture.center.z)));
                float seam = low + height * (.20f + .11f * Mathf.InverseLerp(inner, outer, x));
                deltas[i] = up * ((seam - v.y) * .97f * lateral * vertical * depth);
            }
            mesh.AddBlendShapeFrame("ShowcaseBlink", 100f, deltas, null, null);
        }
    }
}
