using System.Collections;
using System.Linq;
using AnimeCS.Play;
using NUnit.Framework;
using StylizedCharacters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace AnimeCS.Tests
{
    public sealed class RenderReviewTests
    {
        [UnityTest] public IEnumerator ReviewOpensOfflineAndKeepsItsCameraAndRigInsideTheStudio()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Gameplay/LocalMatch.unity");
            var game = Object.FindAnyObjectByType<LocalGame>();
            while (!game.InLobby) yield return null;
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var gameplay = SceneManager.GetActiveScene();
            int cameraMask = Camera.main.cullingMask;

            void Click(string name)
            {
                var button = root.Q<Button>(name);
                Assert.That(button, Is.Not.Null, name);
                using var submit = NavigationSubmitEvent.GetPooled();
                submit.target = button;
                button.SendEvent(submit);
            }

            Click("auth-render-review");
            yield return null;
            yield return new WaitForEndOfFrame();
            var studio = SceneManager.GetActiveScene();
            Assert.That(studio.name, Is.EqualTo("Render Review"));
            Assert.That(game.InLobby, Is.True);
            Assert.That(root.Q("review-roster").Query<Button>(className: "review-card").ToList(), Has.Count.GreaterThanOrEqualTo(8));
            var stage = studio.GetRootGameObjects().Single();
            var camera = stage.GetComponentInChildren<Camera>();
            Assert.That(camera.targetTexture, Is.Not.Null);
            Assert.That(camera.GetUniversalAdditionalCameraData().antialiasing, Is.EqualTo(AntialiasingMode.SubpixelMorphologicalAntiAliasing));
            var firefly = stage.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Single(renderer => renderer.name == "FireflyBody");
            Assert.That(firefly.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord2), Is.True, "projected face SDF");
            Assert.That(firefly.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord3), Is.True, "smoothed outline normals");
            Assert.That(firefly.sharedMaterials.Any(m => m.shader.name == "DanbaidongRP/PBRToon/Eye"), Is.True);
            var width = camera.targetTexture.width;
            yield return LobbyTestServices.Capture("RenderReviewFirefly");

            Click("review-samples-1");
            Assert.That(camera.targetTexture.width, Is.LessThan(width));
            Click("review-samples-0");
            Assert.That(camera.targetTexture.width, Is.EqualTo(width));
            Click("review-character.startraveler");
            Click("review-motion-2");
            Click("review-light-1");
            yield return new WaitForSecondsRealtime(.35f);

            Assert.That(root.Q<Label>("review-character").text, Does.Contain("星临者"));
            var animator = stage.GetComponentInChildren<Animator>();
            Assert.That(animator.avatar.isHuman && animator.avatar.isValid, Is.True);
            Assert.That(animator.GetFloat("Crouch"), Is.GreaterThan(.85f));
            Assert.That(stage.GetComponentInChildren<HumanoidSecondaryMotion>().JointCount, Is.GreaterThan(0));
            var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>();
            int face = System.Array.FindIndex(skin.sharedMaterials, material => material.shader.name == "DanbaidongRP/PBRToon/Face");
            Assert.That(face, Is.GreaterThanOrEqualTo(0));
            Assert.That(skin.sharedMaterials[face].GetTexture("_FaceLightMap").name, Is.EqualTo("StarTravelerFaceSDF"));
            Assert.That(skin.sharedMaterials.Any(material => material.shader.name == "DanbaidongRP/PBRToon/Eye"), Is.True);
            var hair = skin.sharedMaterials.Single(material => material.shader.name == "DanbaidongRP/PBRToon/Hair");
            Assert.That(hair.IsKeywordEnabled("FLAG_HAIRMASK") && hair.GetTexture("_ShadowRampTex"), Is.True);
            var eyeHighlights = skin.sharedMaterials.Where(material => material.name is "StarTraveler_T03" or "StarTraveler_T04").ToArray();
            Assert.That(eyeHighlights, Has.Length.EqualTo(2));
            Assert.That(eyeHighlights.All(material => material.renderQueue >= 3000 && !material.GetShaderPassEnabled("ShadowCaster")), Is.True);
            var properties = new MaterialPropertyBlock();
            skin.GetPropertyBlock(properties, face);
            Assert.That(properties.GetVector("_FaceFrontDirWS").sqrMagnitude, Is.GreaterThan(.9f));
            Assert.That(properties.GetVector("_HeadCenterWS").y, Is.GreaterThan(.8f));
            var fringe = stage.GetComponentsInChildren<SkinnedMeshRenderer>().Single(renderer => renderer.name == "Fringe shadow");
            Assert.That(fringe.sharedMesh.vertexCount, Is.GreaterThan(0));
            Assert.That(fringe.sharedMaterial.GetFloat("_FriStencilComp"), Is.EqualTo((float)CompareFunction.Equal));
            var key = stage.transform.Find("Key");
            var direction = key.forward;
            root.Q<Slider>("review-yaw").value = 45f;
            Assert.Greater(Vector3.Angle(direction, key.forward), 15f);
            root.Q<Slider>("review-pitch").value = 8f;
            var rim = stage.transform.Find("Rim").GetComponent<Light>();
            root.Q<Slider>("review-rim").value = 0f;
            Assert.AreEqual(0f, rim.intensity);
            root.Q<Slider>("review-outline").value = .5f;
            skin.GetPropertyBlock(properties, face);
            Assert.That(properties.GetFloat("_OutlineWidth"), Is.EqualTo(skin.sharedMaterials[face].GetFloat("_OutlineWidth") * .5f).Within(.001f));
            var grading = stage.transform.Find("Global Volume").GetComponent<Volume>();
            root.Q<Slider>("review-exposure").value = -.5f;
            root.Q<Slider>("review-bloom").value = .3f;
            Assert.IsTrue(grading.profile.TryGet<ColorAdjustments>(out var color));
            Assert.IsTrue(grading.profile.TryGet<Bloom>(out var bloom));
            Assert.AreEqual(-.5f, color.postExposure.value);
            Assert.AreEqual(.3f, bloom.intensity.value);
            Assert.IsTrue(grading.sharedProfile.TryGet<ColorAdjustments>(out var authored));
            Assert.AreEqual(0f, authored.postExposure.value);
            var distance = camera.transform.position.z;
            yield return LobbyTestServices.Capture("RenderReviewStarTraveler");
            Click("review-closeup");
            Assert.That(camera.transform.position.z, Is.LessThan(distance - 1f));
            yield return LobbyTestServices.Capture("RenderReviewStarTravelerCloseup");
            Click("review-back");
            yield return null;
            Assert.That(root.Q("render-review-screen"), Is.Null);
            Assert.That(Camera.main.cullingMask, Is.EqualTo(cameraMask));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(gameplay));
        }
    }
}
