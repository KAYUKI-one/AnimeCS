using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StylizedCharacters
{
    /// <summary>Keeps first-person color and full-body shadows on the same animated skeleton.</summary>
    public sealed class FirstPersonVisibility
    {
        private sealed class Part
        {
            internal Renderer Source, Shadow;
            internal SkinnedMeshRenderer Body;
            internal SkinnedMeshRenderer Color;
            internal Mesh Full, Local;
            internal byte[] Mask;
            internal int Layer;
            internal ShadowCastingMode Cast;
            internal bool Prepared, OwnsMesh;
        }

        private readonly List<Part> _parts = new();
        private readonly int _hidden = LayerMask.NameToLayer(CharacterCamera.HiddenLayer);
        public bool Local { get; private set; }

        /// <summary>Registers a complete renderer to conceal. It retains a shadow on its original layer.</summary>
        public void Conceal(Renderer renderer) => Add(renderer, null);

        /// <summary>Registers a readable skinned mesh and the packed mask of visible vertices baked for it.</summary>
        public void Split(SkinnedMeshRenderer renderer, byte[] mask) => Add(renderer, mask);

        /// <summary>Registers an editor-baked mesh with the same skin, blend shapes and material slots.</summary>
        public void Split(SkinnedMeshRenderer renderer, Mesh mesh) => Add(renderer, null, mesh);

        private void Add(Renderer renderer, byte[] mask, Mesh local = null)
        {
            if (!renderer) return;
            var body = renderer as SkinnedMeshRenderer;
            _parts.Add(new Part { Source = renderer, Body = body, Full = body ? body.sharedMesh : null,
                Mask = mask, Local = local, Cast = renderer.shadowCastingMode, Layer = renderer.gameObject.layer });
        }

        public void SetLocal(bool local)
        {
            if (Local == local) return;
            Local = local;
            foreach (var part in _parts)
            {
                if (!part.Source) continue;
                if (local)
                {
                    Prepare(part);
                    if (_hidden >= 0) part.Source.gameObject.layer = _hidden;
                    part.Source.shadowCastingMode = _hidden >= 0 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly;
                    if (part.Shadow) part.Shadow.enabled = part.Source.enabled;
                    if (part.Color) part.Color.enabled = part.Source.enabled;
                }
                else
                {
                    part.Source.gameObject.layer = part.Layer;
                    part.Source.shadowCastingMode = part.Cast;
                    if (part.Shadow) part.Shadow.enabled = false;
                    if (part.Color) part.Color.enabled = false;
                }
            }
        }

        private void Prepare(Part part)
        {
            if (part.Prepared) return;
            part.Prepared = true;
            if (!part.Local && part.Full && part.Full.isReadable && part.Mask?.Length == (part.Full.vertexCount + 7) / 8)
            {
                part.Local = MakeVisibleMesh(part.Full, part.Mask);
                part.OwnsMesh = true;
            }
            if (part.Local)
                part.Color = CopyRenderer(part, part.Local, "First-person body", ShadowCastingMode.Off) as SkinnedMeshRenderer;
            if (part.Cast == ShadowCastingMode.Off || _hidden < 0) return;
            // The shadow renderer stays on the world layer even when the color renderer is culled.
            part.Shadow = CopyRenderer(part, part.Full, "Full character shadow", ShadowCastingMode.ShadowsOnly);
        }

        public static Mesh MakeVisibleMesh(Mesh source, byte[] mask)
        {
            var mesh = Object.Instantiate(source);
            mesh.name = source.name + " (first person)";
            var retained = new List<int>();
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] indices = source.GetTriangles(sub);
                retained.Clear();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    if (!Visible(a) || !Visible(b) || !Visible(c)) continue;
                    retained.Add(a); retained.Add(b); retained.Add(c);
                }
                mesh.SetTriangles(retained, sub, false);
            }
            return mesh;

            bool Visible(int index) => (mask[index >> 3] & (1 << (index & 7))) != 0;
        }

        private static Renderer CopyRenderer(Part part, Mesh mesh, string name, ShadowCastingMode mode)
        {
            var pivot = new GameObject(name) { layer = part.Layer };
            pivot.transform.SetParent(part.Source.transform, false);
            Renderer renderer = null;
            if (part.Body)
            {
                var skin = pivot.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.bones = part.Body.bones;
                skin.rootBone = part.Body.rootBone;
                skin.localBounds = part.Body.localBounds;
                skin.quality = part.Body.quality;
                renderer = skin;
            }
            else if (part.Source is MeshRenderer && part.Source.TryGetComponent<MeshFilter>(out var filter))
            {
                pivot.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                renderer = pivot.AddComponent<MeshRenderer>();
            }
            if (!renderer) { Destroy(pivot); return null; }
            renderer.sharedMaterials = part.Source.sharedMaterials;
            renderer.renderingLayerMask = part.Source.renderingLayerMask;
            renderer.lightProbeUsage = part.Source.lightProbeUsage;
            renderer.probeAnchor = part.Source.probeAnchor;
            renderer.receiveShadows = part.Source.receiveShadows;
            renderer.shadowCastingMode = mode;
            return renderer;
        }

        /// <summary>Call after evaluated blend shapes have been applied to the animated renderers.</summary>
        public void SyncShadows()
        {
            if (!Local) return;
            foreach (var part in _parts)
                if (part.Body && (part.Color || part.Shadow))
                    for (int shape = 0; shape < part.Full.blendShapeCount; shape++)
                    {
                        float weight = part.Body.GetBlendShapeWeight(shape);
                        if (part.Color) part.Color.SetBlendShapeWeight(shape, weight);
                        if (part.Shadow is SkinnedMeshRenderer shadow) shadow.SetBlendShapeWeight(shape, weight);
                    }
        }

        public void Release()
        {
            SetLocal(false);
            foreach (var part in _parts)
            {
                if (part.OwnsMesh && part.Local) Destroy(part.Local);
                if (part.Shadow) Destroy(part.Shadow.gameObject);
                if (part.Color) Destroy(part.Color.gameObject);
            }
            _parts.Clear();
        }

        private static void Destroy(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
