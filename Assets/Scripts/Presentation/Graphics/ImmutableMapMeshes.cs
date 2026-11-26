using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace AnimeCS.Play
{
    // Authored on display-only map imports. Navigation and collision use separate meshes.
    [DisallowMultipleComponent]
    public sealed class ImmutableMapMeshes : MonoBehaviour
    {
        internal IEnumerator ReleaseCpuCopies()
        {
            if (Application.isEditor || SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) yield break;
            double deadline=Time.realtimeSinceStartupAsDouble+.003;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if (!mesh || !mesh.isReadable) continue;
                mesh.UploadMeshData(true);
                if (Time.realtimeSinceStartupAsDouble<deadline) continue;
                yield return null;
                deadline=Time.realtimeSinceStartupAsDouble+.003;
            }
        }
    }
}
