using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AnimeCS.Tests
{
    public sealed class HorizonDepthTests
    {
        [UnityTest]
        public IEnumerator HorizonGroundDrawsOverSkyButNeverOverCloserDepth()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");

            const int size = 64;
            const int isolatedLayer = 30;
            var sandShader = Shader.Find("AnimeCS/Horizon Ground");
            var wallShader = Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(sandShader, Is.Not.Null);
            Assert.That(wallShader, Is.Not.Null);

            var rig = new GameObject("Horizon depth GPU probe");
            var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var readback = new Texture2D(size, size, TextureFormat.RGB24, false);
            var sandMaterial = new Material(sandShader);
            var wallMaterial = new Material(wallShader);
#if UNITY_EDITOR
            bool asyncShaders=UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation=false;
#endif
            try
            {
                rig.transform.position = new Vector3(1200f, 100f, 1200f);
                var camera = rig.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << isolatedLayer;
                camera.fieldOfView = 60f;
                camera.farClipPlane = 50f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.targetTexture = target;

                wallMaterial.SetColor("_BaseColor", Color.red);
                AddProbe("Opaque wall", new Vector3(0f, 0f, 8f), new Vector3(2f, 2f, .1f), wallMaterial);
                sandMaterial.SetColor("_BaseColor", Color.white);
                var sand = AddProbe("Horizon sand", new Vector3(0f, 0f, 3f), new Vector3(6f, 6f, .1f), sandMaterial);
                sand.SetActive(false);

                Color wallBefore=default,skyBefore=default,wallAfter=default,skyAfter=default;
                bool baseline=false;
                double deadline=Time.realtimeSinceStartupAsDouble+8;
                do
                {
                    yield return new WaitForEndOfFrame();
                    (wallBefore,skyBefore)=Sample();
                    baseline=wallBefore.r>.8f && wallBefore.g<.1f && wallBefore.b<.1f && Difference(skyBefore,Color.black)<.05f;
                } while (!baseline && Time.realtimeSinceStartupAsDouble<deadline);
                Assert.That(baseline,Is.True,$"Expected a red wall and black clear color; wall={wallBefore}, sky={skyBefore}.");

                sand.SetActive(true);
                deadline=Time.realtimeSinceStartupAsDouble+8;
                do
                {
                    yield return new WaitForEndOfFrame();
                    (wallAfter,skyAfter)=Sample();
                } while (Difference(skyAfter,skyBefore)<=.06f && Time.realtimeSinceStartupAsDouble<deadline);
                Assert.That(Difference(skyAfter, skyBefore), Is.GreaterThan(.06f),
                    $"Horizon sand did not appear over the clear region. Sky before={skyBefore}, after={skyAfter}.");
                Assert.That(Difference(wallAfter, wallBefore), Is.LessThanOrEqualTo(2f / 255f),
                    $"Horizon sand covered the wall. Wall before={wallBefore}, after={wallAfter}; sky before={skyBefore}, after={skyAfter}.");

                GameObject AddProbe(string name, Vector3 position, Vector3 scale, Material material)
                {
                    var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    probe.name = name;
                    probe.layer = isolatedLayer;
                    probe.transform.SetParent(rig.transform, false);
                    probe.transform.localPosition = position;
                    probe.transform.localScale = scale;
                    probe.GetComponent<Renderer>().sharedMaterial = material;
                    return probe;
                }

                (Color wall, Color sky) Sample()
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        readback.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                        return (readback.GetPixel(size / 2, size / 2), readback.GetPixel(size / 8, size / 2));
                    }
                    finally { RenderTexture.active = previous; }
                }
            }
            finally
            {
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation=asyncShaders;
#endif
                rig.SetActive(false);
                target.Release();
                Object.Destroy(rig);
                Object.Destroy(target);
                Object.Destroy(readback);
                Object.Destroy(sandMaterial);
                Object.Destroy(wallMaterial);
            }
        }

        private static float Difference(Color a, Color b) =>
            Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
    }
}
