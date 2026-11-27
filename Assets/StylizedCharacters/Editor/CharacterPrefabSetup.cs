using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StylizedCharacters.Editor
{
    public static class CharacterPrefabSetup
    {
        private const string Menu = "Assets/Stylized Characters/Create Playable Humanoid";
        public const string ControllerPath = "Assets/StylizedCharacters/Animation/Locomotion.controller";

        [MenuItem(Menu, true)]
        private static bool CanPrepare() => Selection.objects.OfType<GameObject>().Any(EditorUtility.IsPersistent);

        [MenuItem(Menu)]
        private static void PrepareSelected()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            var results = Selection.objects.OfType<GameObject>().Where(EditorUtility.IsPersistent)
                .Select(source => Create(source, controller)).Where(prefab => prefab).ToArray();
            if (results.Length > 0) Selection.objects = results;
        }

        /// <summary>Builds a standalone player with a humanoid avatar, camera, motor and baked render settings.</summary>
        public static GameObject Create(GameObject source, RuntimeAnimatorController controller) =>
            Prepare(source, controller, true);

        /// <summary>Builds a reusable visual prefab for a host which supplies its own movement and camera.</summary>
        public static GameObject PrepareVisual(GameObject source, RuntimeAnimatorController controller)
        {
            if (source.TryGetComponent<PreparedCharacter>(out _) && !source.TryGetComponent<CharacterPlayer>(out _)) return source;
            return Prepare(source, controller, false);
        }

        private static GameObject Prepare(GameObject source, RuntimeAnimatorController controller, bool playable)
        {
            string path = AssetDatabase.GetAssetPath(source);
            string sourceGuid = AssetDatabase.AssetPathToGUID(path);
            string folder = path.StartsWith("Assets/") ? Path.GetDirectoryName(path).Replace('\\','/') : "Assets";
            string label = source.name + (playable ? " Player" : " Visual");
            string output = $"{folder}/{label}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(output);
            if (existing)
            {
                if (!existing.TryGetComponent<PreparedCharacter>(out var prior) || prior.SourceGuid != sourceGuid)
                    output = AssetDatabase.GenerateUniqueAssetPath(output);
                else if (!playable && prior.SourceRevision == AssetDatabase.GetAssetDependencyHash(path).ToString())
                    return existing;
            }
            string meshFolder = Path.ChangeExtension(output, null) + " Meshes";
            var styled = CharacterModelMaterials.Prepare(source);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(label);
                SceneManager.MoveGameObjectToScene(root, scene);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(styled, scene);
                model.transform.SetParent(root.transform, false);
                var avatar = model.GetComponentInChildren<Animator>(true);
                if (!HumanoidImport.Valid(avatar))
                {
                    var generated = HumanoidImport.CreateAvatar(model, out var missing);
                    if (!generated || !generated.isValid || !generated.isHuman)
                    {
                        if (generated) Object.DestroyImmediate(generated);
                        Debug.LogError(missing.Length > 0
                            ? $"{source.name}: 缺少可识别的 Humanoid 骨骼，{string.Join("、", missing)}。"
                            : $"{source.name}: 骨骼命名已识别，但骨架拓扑或静止姿势无法生成有效 Humanoid Avatar。", source);
                        return null;
                    }
                    if (avatar && avatar.gameObject != model) avatar.enabled = false;
                    avatar = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                    generated.name = source.name + " Humanoid";
                    avatar.avatar = Save(generated, "Avatar");
                }
                if (controller) avatar.runtimeAnimatorController = controller;
                avatar.applyRootMotion = false;
                var head = avatar.GetBoneTransform(HumanBodyBones.Head);
                var all = model.GetComponentsInChildren<Renderer>(true);
                var hips = avatar.GetBoneTransform(HumanBodyBones.Hips);
                var leftFoot = avatar.GetBoneTransform(HumanBodyBones.LeftFoot);
                var rightFoot = avatar.GetBoneTransform(HumanBodyBones.RightFoot);
                float rigHeight = head.position.y - Mathf.Min(leftFoot.position.y, rightFoot.position.y);
                if (rigHeight < .55f || rigHeight > 2.6f)
                    model.transform.localScale *= 1.46f / Mathf.Max(.0001f, rigHeight);
                float floor = all.Length == 0 ? 0 : all.Min(r => r.bounds.min.y);
                model.transform.localPosition -= new Vector3(hips.position.x, floor, hips.position.z);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

                var springs = HumanoidSpringImport.Configure(avatar);
                var prepared = root.AddComponent<PreparedCharacter>();
                var data = new SerializedObject(prepared);
                data.FindProperty("sourceGuid").stringValue = sourceGuid;
                data.FindProperty("sourceRevision").stringValue = AssetDatabase.GetAssetDependencyHash(path).ToString();
                var visible = new List<PreparedCharacter.Part>();
                var hide = new List<Renderer>();
                foreach (var renderer in all)
                {
                    if (renderer is SkinnedMeshRenderer skin &&
                        FirstPersonMeshBake.Create(avatar, skin) is { Length: >0 } mask && mask.Any(bits => bits != 0))
                    {
                        var mesh = FirstPersonVisibility.MakeVisibleMesh(skin.sharedMesh, mask);
                        if (Enumerable.Range(0, mesh.subMeshCount).Any(sub => mesh.GetIndexCount(sub) > 0))
                        {
                            mesh = Save(mesh, $"FirstPerson-{visible.Count + 1}");
                            visible.Add(new PreparedCharacter.Part { renderer = skin, mesh = mesh });
                            continue;
                        }
                        Object.DestroyImmediate(mesh);
                    }
                    hide.Add(renderer);
                }
                if (visible.Count == 0)
                {
                    Debug.LogError($"{source.name}: 找到了人体骨架，但没有可烘焙的手臂蒙皮。请检查 FBX 的 Skin 和手臂权重。", source);
                    return null;
                }
                BindParts(data, visible, hide);
                if (playable) AddPlayer(root, avatar, head, prepared);
                var result = PrefabUtility.SaveAsPrefabAsset(root, output);
                Debug.Log($"{source.name}: Humanoid 已就绪，可见蒙皮 {visible.Count} 个，头发或头纱关节 {springs.hair} 个，衣摆关节 {springs.cloth} 个。", result);
                return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }

            T Save<T>(T asset, string key) where T : Object
            {
                if (!AssetDatabase.IsValidFolder(meshFolder))
                    AssetDatabase.CreateFolder(folder, Path.GetFileName(meshFolder));
                string address = $"{meshFolder}/{key}.asset";
                var saved = AssetDatabase.LoadAssetAtPath<T>(address);
                if (!saved) { AssetDatabase.CreateAsset(asset, address); return asset; }
                EditorUtility.CopySerialized(asset, saved);
                Object.DestroyImmediate(asset);
                EditorUtility.SetDirty(saved);
                AssetDatabase.SaveAssetIfDirty(saved);
                return saved;
            }
        }

        private static void AddPlayer(GameObject root, Animator avatar, Transform head, PreparedCharacter prepared)
        {
            var capsule = root.AddComponent<CharacterController>();
            capsule.height = Mathf.Max(.75f, head.position.y + .20f);
            capsule.radius = Mathf.Min(.29f, capsule.height * .19f);
            capsule.center = Vector3.up * capsule.height * .5f;
            var camera = new GameObject("Character Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.nearClipPlane = CharacterCamera.NearClip;
            var player = root.AddComponent<CharacterPlayer>();
            var data = new SerializedObject(player);
            data.FindProperty("avatar").objectReferenceValue = avatar;
            data.FindProperty("view").objectReferenceValue = camera;
            data.FindProperty("prepared").objectReferenceValue = prepared;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindParts(SerializedObject data, List<PreparedCharacter.Part> visible, List<Renderer> hide)
        {
            var body = data.FindProperty("body");
            body.arraySize = visible.Count;
            for (int i = 0; i < visible.Count; i++)
            {
                var record = body.GetArrayElementAtIndex(i);
                record.FindPropertyRelative("renderer").objectReferenceValue = visible[i].renderer;
                record.FindPropertyRelative("mesh").objectReferenceValue = visible[i].mesh;
            }
            var hidden = data.FindProperty("hideInFirstPerson");
            hidden.arraySize = hide.Count;
            for (int i = 0; i < hide.Count; i++) hidden.GetArrayElementAtIndex(i).objectReferenceValue = hide[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
