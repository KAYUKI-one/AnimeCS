using System.Collections;
using System.IO;
using System.Linq;
using AnimeCS.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    public sealed class RenderReviewCaptureTests
    {
        [UnityTest] public IEnumerator CaptureReferenceLighting()
        {
            var folder = System.Environment.GetEnvironmentVariable("REVIEW_CAPTURE_DIR");
            if (string.IsNullOrEmpty(folder)) Assert.Ignore("Set REVIEW_CAPTURE_DIR for graphical review.");
            Directory.CreateDirectory(folder);
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            void Click(string name)
            {
                var button = root.Q<Button>(name);
                Assert.IsNotNull(button, name);
                using var submit = NavigationSubmitEvent.GetPooled();
                submit.target = button;
                button.SendEvent(submit);
            }
            Click("auth-render-review");
            yield return null;
            var camera = SceneManager.GetActiveScene().GetRootGameObjects().Single().GetComponentInChildren<Camera>();
            Click("review-closeup");
            Click("review-pause");
            foreach (string actor in new[]{"firefly","lily","mishty"})
            {
                Click("review-character." + actor);
                for (int light = 0; light < 2; light++)
                {
                    Click("review-light-" + light);
                    string path = Path.Combine(folder, actor + (light == 0 ? "-studio" : "-daylight"));
                    var original = camera.targetTexture;
                    var post = camera.GetUniversalAdditionalCameraData();
                    foreach (bool hdr in new[]{false,true})
                    {
                        var rt = RenderTexture.GetTemporary(960,720,24,hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32,hdr ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                        camera.targetTexture = rt;
                        post.renderPostProcessing = !hdr;
                        for (int frame=0; frame<12; frame++) yield return null;
                        var previous = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(rt.width,rt.height,hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32,false,hdr);
                        tex.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
                        tex.Apply();
                        if (hdr) File.WriteAllBytes(path+".rgba16f",tex.GetRawTextureData());
                        else File.WriteAllBytes(path+".png",tex.EncodeToPNG());
                        Object.Destroy(tex);
                        RenderTexture.active=previous;
                        camera.targetTexture=original;
                        RenderTexture.ReleaseTemporary(rt);
                    }
                    post.renderPostProcessing=true;
                }
            }
            if (System.Environment.GetEnvironmentVariable("REVIEW_CAPTURE_MOTION") == "1")
            {
                Click("review-full");
                Click("review-light-0");
                Click("review-pause");
                foreach (string actor in new[] { "lily", "mishty", "firefly" })
                {
                    Click("review-character." + actor);
                    var rig = camera.transform.parent.GetComponentInChildren<Animator>();
                    foreach (var (button, label) in new[] { ("review-motion-3", "walk"), ("review-motion-1", "run"), ("review-turn", "turn") })
                    {
                        Click(button);
                        yield return new WaitForSecondsRealtime(label == "turn" ? .16f : .42f);
                        SaveView(camera, Path.Combine(folder, actor + "-" + label + ".png"));
                    }
                    Click("review-turn");
                    Click("review-motion-0");
                    Click("review-closeup");
                    yield return new WaitForSecondsRealtime(.5f);
                    var skin = rig.GetComponentsInChildren<SkinnedMeshRenderer>()
                        .Single(r => r.sharedMesh.GetBlendShapeIndex("ShowcaseBlink") >= 0);
                    int shape = skin.sharedMesh.GetBlendShapeIndex("ShowcaseBlink");
                    rig.Play("Eyelids.Blink", 1, 2.235f / 7.4f);
                    rig.Update(0f);
                    Assert.Greater(skin.GetBlendShapeWeight(shape), 90f, actor + " eyelid binding");
                    Click("review-pause");
                    yield return null;
                    yield return null;
                    SaveView(camera, Path.Combine(folder, actor + "-blink.png"));
                    Click("review-pause");
                    Click("review-full");
                }
            }
            Click("review-back");
        }

        private static void SaveView(Camera camera, string path)
        {
            var previous = RenderTexture.active;
            var rt = camera.targetTexture;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(tex);
            }
        }
    }
}
