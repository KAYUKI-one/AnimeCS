using System;
using UnityEngine;

namespace StylizedCharacters
{
    /// <summary>Per-prefab render bindings generated from a skeletal model during import.</summary>
    [DisallowMultipleComponent]
    public sealed class PreparedCharacter : MonoBehaviour
    {
        [Serializable]
        public struct Part
        {
            public SkinnedMeshRenderer renderer;
            public Mesh mesh;
        }

        [SerializeField] private Part[] body = Array.Empty<Part>();
        [SerializeField] private Renderer[] hideInFirstPerson = Array.Empty<Renderer>();
        [SerializeField] private string sourceGuid;
        [SerializeField] private string sourceRevision;
        public Part[] Body => body;
        public Renderer[] Hidden => hideInFirstPerson;
        public string SourceGuid => sourceGuid;
        public string SourceRevision => sourceRevision;

        public void Bind(FirstPersonVisibility view)
        {
            foreach (var part in body) view.Split(part.renderer, part.mesh);
            foreach (var renderer in hideInFirstPerson) view.Conceal(renderer);
        }
    }
}
