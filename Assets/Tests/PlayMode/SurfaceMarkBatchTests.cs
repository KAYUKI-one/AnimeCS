using System.Collections;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace AnimeCS.Tests
{
    public sealed class SurfaceMarkBatchTests
    {
        [UnityTest]
        public IEnumerator RendersTheFinalInstanceInA2048MarkBatchAndClearsIt()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsInstancing)
                Assert.Ignore("Requires an instancing-capable graphics device.");
            var cameraObject = new GameObject("Decal GPU probe");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(5000,5000,5000);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.farClipPlane = 10;
            camera.allowHDR = camera.allowMSAA = false;
            var target = new RenderTexture(32,32,24);
            var pixel = new Texture2D(1,1);
            camera.targetTexture = target;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor",Color.red);
            var batch = new SurfaceMarkBatch(material,2048);
            try
            {
                var center = camera.transform.position + Vector3.forward * 4;
                for (int i=0; i<2047; i++)
                    batch.Add(center + Vector3.right * 15, Vector3.back, 1);
                batch.Add(center,Vector3.back,1);
                Assert.AreEqual(2048,batch.Count);
                batch.Draw();
                yield return new WaitForEndOfFrame();
                Color seen = Read();
                Assert.Greater(seen.r,.45f);
                Assert.Less(seen.g,.2f);
                batch.Reset();
                batch.Draw();
                yield return new WaitForEndOfFrame();
                Assert.Less(Read().r,.12f);
            }
            finally
            {
                batch.Dispose();
                camera.targetTexture = null;
                Object.Destroy(cameraObject); Object.Destroy(pixel); Object.Destroy(target); Object.Destroy(material);
            }

            Color Read()
            {
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                try
                {
                    pixel.ReadPixels(new Rect(16,16,1,1),0,0);
                    pixel.Apply();
                    return pixel.GetPixel(0,0);
                }
                finally { RenderTexture.active = previous; }
            }
        }
    }
}
