using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnimeCS.Content;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    // Artist-triggered setup for the bundled Blender exports. Existing definitions keep their stable IDs.
    public static class CastArtImport
    {
        private const string Art = "Assets/Content/Art";
        private const string MotionPath = Art + "/Characters/Locomotion.controller";
        [Serializable]
        private sealed class Member
        {
            public string name = "", folder = "", displayName = "", faction = "";
            public int playOrder = -1;
            public float headwearHeight = .064f;
            public bool animationDonor = false;
            public string BodyPath => $"{Art}/Characters/{folder}/{name}.fbx";
        }

        [Serializable]
        private sealed class Roster
        {
            public Member[] members = Array.Empty<Member>();
        }

        private static Member[] PlayableCast()
        {
            var roster = Load<TextAsset>("Assets/Editor/CastRoster.json");
            return JsonUtility.FromJson<Roster>(roster.text).members.Where(member => member.playOrder >= 0)
                .OrderBy(member => member.playOrder).ToArray();
        }

        [MenuItem("AnimeCS/美术/同步随附角色与展示枪模")]
        public static void Synchronize()=>Synchronize(false);

        [MenuItem("AnimeCS/美术/同步角色与服饰")]
        public static void SynchronizeCharacters()=>Synchronize(true);
        private static void Synchronize(bool castOnly)
        {
            var catalog = ContentLibrary.Catalog;
            var outfits = new List<OutfitDefinition>();
            var cast = PlayableCast();
            foreach(string oldId in new[]{"character.operator","outfit.station","outfit.nightshift","character.cantarella","outfit.cantarella",
                "character.blackmiku","outfit.blackmiku","character.laevatain","outfit.laevatain"})
            {var old=catalog.Find<ContentDefinition>(oldId);if(!old)continue;var entry=new SerializedObject(old);entry.FindProperty("_archived").boolValue=true;entry.ApplyModifiedPropertiesWithoutUndo();}
            foreach (var member in cast)
            {
                ConfigureHuman(member.BodyPath, member.animationDonor);
                var sleeves = member.BodyPath.Replace(".fbx", "ViewRig.fbx");
                ConfigureHuman(sleeves,false,((ModelImporter)AssetImporter.GetAtPath(member.BodyPath)).humanDescription.human);
                // An arms-only mesh has no head weights. Keep its own bind transforms and reuse the body's complete mapping.
                var sleeveAnimator = Load<GameObject>(sleeves).GetComponent<Animator>();
                if (!sleeveAnimator || !sleeveAnimator.isHuman)
                {
                    var importer = (ModelImporter)AssetImporter.GetAtPath(sleeves);
                    var mapping = importer.humanDescription;
                    mapping.human = ((ModelImporter)AssetImporter.GetAtPath(member.BodyPath)).humanDescription.human;
                    importer.humanDescription = mapping;
                    importer.SaveAndReimport();
                }
                Remap(sleeves, true);
                Remap(member.BodyPath, true);
                Remap(member.BodyPath.Replace(".fbx", "Arms.fbx"), true);
                Remap(member.BodyPath.Replace(".fbx", "SidearmArms.fbx"), true);
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(MotionPath);
            if (!controller) controller = CreateController(cast.Single(member => member.animationDonor).BodyPath);
            LocalBodyBake.ConfigureLayers(controller);
            foreach (var member in cast)
            {
                string name = member.name, key = name.ToLowerInvariant();
                var body = Definition<CharacterDefinition>(catalog, "Characters", name, "character." + key, member.displayName);
                var serialized = new SerializedObject(body);
                serialized.FindProperty("_faction").enumValueIndex=(int)Enum.Parse<Side>(member.faction);
                Set(serialized, "_model", Load<GameObject>(member.BodyPath));
                Set(serialized, "_controller", controller);
                CharacterFringeBake.Bind(serialized, member.BodyPath);
                var picture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Content/UI/Portraits/" + name + ".png");
                if (picture) Set(serialized, "_portrait", picture);
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Content/UI/Portraits/" + name + "Icon.png");
                if (icon) Set(serialized, "_icon", icon);
                var standard = catalog.Find<CharacterDefinition>("character.operator");
                if (standard)
                {
                    Set(serialized, "_attackInsignia", standard.Insignia(Side.Attack));
                    Set(serialized, "_defendInsignia", standard.Insignia(Side.Defend));
                }
                AssignTeamMarks(name, serialized);
                serialized.FindProperty("_headwearOffset").vector3Value=Vector3.up * member.headwearHeight;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var outfit = Definition<OutfitDefinition>(catalog, "Outfits", name, "outfit." + key, member.displayName);
                serialized = new SerializedObject(outfit);
                Set(serialized, "_character", body);
                Set(serialized, "_viewRig", Load<GameObject>(member.BodyPath.Replace(".fbx","ViewRig.fbx")));
                Set(serialized, "_arms", Load<GameObject>(member.BodyPath.Replace(".fbx","Arms.fbx")));
                Set(serialized, "_sidearmArms", Load<GameObject>(member.BodyPath.Replace(".fbx","SidearmArms.fbx")));
                serialized.FindProperty("_included").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var sharedPose = new SerializedObject(outfit);
                sharedPose.FindProperty("_stanceController").objectReferenceValue=null;
                sharedPose.ApplyModifiedPropertiesWithoutUndo();
                CastEyeCalibration.Bind(outfit);
                outfits.Add(outfit);
            }
            var data = new SerializedObject(catalog);
            Set(data, "_character", outfits[0].Character);
            Set(data, "_outfit", outfits[0]);
            foreach(var extra in catalog.SeatOutfits)
                if(extra && !extra.Archived && extra.Character && !extra.Character.Archived && !outfits.Contains(extra))outfits.Add(extra);
            var pool = data.FindProperty("_seatOutfits");
            pool.arraySize = outfits.Count;
            for (int i = 0; i < outfits.Count; i++) pool.GetArrayElementAtIndex(i).objectReferenceValue = outfits[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            if(castOnly) { WearableBake.Bake(catalog,outfits.Take(cast.Length).ToList()); AssetDatabase.SaveAssets(); return; }
            // Contact points are authored on the imported mesh after its original FBX axis conversion.
            // The right knuckles sit outside the rear grip. The support knuckles follow the forend or the other hand.
            var fits = new (string name, Vector3 grip, Vector3 support)[] {
                ("Street20",new(.024f,-.012f,.027f),new(-.031f,-.024f,.037f)),
                ("Whitefalcon12s",new(.024f,-.015f,.022f),new(-.030f,-.027f,.034f)),
                ("Verdict57",new(.026f,-.017f,.041f),new(-.032f,-.030f,.054f)),
                ("Dash9",new(.027f,.007f,.025f),new(-.032f,-.006f,.039f)),
                ("Heavy50",new(.024f,-.013f,.045f),new(-.030f,-.028f,.056f)),
                ("Ember47",new(.022f,.010f,.022f),new(.022f,.075f,.236f)),
                ("Watch41S",new(.021f,-.007f,-.024f),new(.026f,.075f,.156f)),
                ("Fold44",new(.022f,-.002f,.080f),new(.027f,.076f,.278f)),
                ("Feather08",new(.023f,-.027f,-.006f),new(.026f,-.015f,.192f)),
                ("Longglass96",new(.031f,-.026f,-.018f),new(.031f,.003f,.208f)),
                ("Hummingbird9",new(.024f,.002f,.051f),new(-.024f,.004f,.168f)),
                ("Spark10",new(.029f,.002f,.029f),new(-.028f,.065f,.169f))
            };
            foreach (var (name, grip, support) in fits)
            {
                string path = $"{Art}/Weapons/{name}/{name}.fbx";
                Remap(path, false);
                var weapon = catalog.Find<WeaponDefinition>("weapon." + name.ToLowerInvariant());
                var item = new SerializedObject(weapon);
                Set(item, "_worldModel", Load<GameObject>(path));
                item.FindProperty("_grip").vector3Value = grip;
                item.FindProperty("_support").vector3Value = support;
                item.FindProperty("_uprightSupport").boolValue = name=="Hummingbird9" || name=="Spark10";
                item.FindProperty("_shoulderPad").vector3Value = name switch {
                    "Ember47"=>new(0,.055f,-.255f), "Fold44"=>new(0,.060f,-.256f),
                    "Watch41S"=>new(0,.070f,-.260f), "Feather08"=>new(0,.012f,-.293f),
                    "Longglass96"=>new(0,-.010f,-.319f), "Hummingbird9"=>new(0,.034f,-.304f),
                    _=>Vector3.zero };
                item.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var (name, finish) in new[] { ("Street20","Vogue"), ("Whitefalcon12s","Cortex"),
                ("Verdict57","Angry Mob"), ("Dash9","Fuel Injector"), ("Hummingbird9","Starlight Protector"),
                ("Spark10","Disco Tech"), ("Fold44","Temukau") })
            {
                var key=name.ToLowerInvariant();
                var weapon=catalog.Find<WeaponDefinition>("weapon."+key);
                var skin=Definition<WeaponSkinDefinition>(catalog,"Skins",name+"Finish","skin."+key+".showcase",finish);
                var dataSkin=new SerializedObject(skin);
                Set(dataSkin,"_weapon",weapon);
                Set(dataSkin,"_material",Load<Material>($"{Art}/Weapons/{name}/{name}_T00.mat"));
                dataSkin.ApplyModifiedPropertiesWithoutUndo();
                var dataGun=new SerializedObject(weapon);
                Set(dataGun,"_defaultSkin",skin); dataGun.ApplyModifiedPropertiesWithoutUndo();
            }
            var ember=catalog.Find<WeaponDefinition>("weapon.ember47");
            var caseSkin=Definition<WeaponSkinDefinition>(catalog,"Skins","EmberCase","skin.ember47.case","表面淬火 · 试玩");
            var caseData=new SerializedObject(caseSkin);Set(caseData,"_weapon",ember);
            Set(caseData,"_texture",Load<Texture2D>(Art+"/Weapons/Ember47/Ember47Case.png"));
            caseData.FindProperty("_included").boolValue=true;caseData.ApplyModifiedPropertiesWithoutUndo();
            var knife=Definition<WeaponDefinition>(catalog,"Weapons","RubyEdge","weapon.rubyedge","绯纹刺刀");
            string knifePath=Art+"/Weapons/RubyEdge/RubyEdge.fbx";Remap(knifePath,false);
            var knifeData=new SerializedObject(knife);Set(knifeData,"_worldModel",Load<GameObject>(knifePath));
            knifeData.FindProperty("_class").enumValueIndex=(int)WeaponClass.Knife;
            knifeData.FindProperty("_price").intValue=0;knifeData.FindProperty("_runSpeed").floatValue=6.35f;
            knifeData.FindProperty("_grip").vector3Value=new Vector3(-.013f,.014f,-.017f);
            knifeData.ApplyModifiedPropertiesWithoutUndo();
            var knifeCatalog=new SerializedObject(catalog);Set(knifeCatalog,"_knife",knife);knifeCatalog.ApplyModifiedPropertiesWithoutUndo();
            WearableBake.Bake(catalog,outfits.Take(cast.Length).ToList());
            string stage = Art + "/Lobby/Concourse.fbx";
            Remap(stage, false);
            var style = Load<AnimeCS.Play.MatchPresentation>("Assets/Content/UI/Competitive.asset");
            style.lobbySet = Load<GameObject>(stage);
            style.lobbyWeapon = catalog.Find<WeaponDefinition>("weapon.fold44");
            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureHuman(string path, bool donor,HumanBone[] bodyMap=null)
        {
            var im = (ModelImporter)AssetImporter.GetAtPath(path);
            bool setup = im.animationType != ModelImporterAnimationType.Human || im.importAnimation != donor ||
                (donor && !im.clipAnimations.Any(c => c.name == "RunForward"));
            if (!setup) return;
            im.animationType = ModelImporterAnimationType.Human;
            im.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            im.importAnimation = donor;
            if(bodyMap!=null){var mapped=im.humanDescription;mapped.human=bodyMap;im.humanDescription=mapped;}
            if (donor)
            {
                var clips = im.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = c.name.Split('|').Last();
                    c.loopTime = true;
                    c.lockRootRotation = c.lockRootPositionXZ = c.lockRootHeightY = true;
                    c.keepOriginalOrientation = c.keepOriginalPositionXZ = c.keepOriginalPositionY = true;
                }
                im.clipAnimations = clips;
            }
            im.SaveAndReimport();
        }

        internal static void Remap(string path, bool character)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var folder = Path.GetDirectoryName(path).Replace('\\','/');
            var changes = false;
            var materials = Load<GameObject>(path).GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToArray();
            var existing = importer.GetExternalObjectMap();
            foreach (var original in materials)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), original.name);
                string matPath = folder + "/" + original.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (!m)
                {
                    m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    m.name = original.name;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + original.name + ".png");
                    if (!texture && character)
                    {
                        string actor = Path.GetFileNameWithoutExtension(path);
                        if (original.name.EndsWith("_Lash", StringComparison.OrdinalIgnoreCase))
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + actor + "_T01.png");
                        else if (original.name.EndsWith("_Skin", StringComparison.OrdinalIgnoreCase) ||
                                 original.name.EndsWith("_Stockings", StringComparison.OrdinalIgnoreCase))
                            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + actor + "_T03.png");
                    }
                    Color tint = Color.white;
                    if (!texture && !ColorUtility.TryParseHtmlString("#" + original.name.Split('_').Last(), out tint)) tint = Color.white;
                    m.SetColor("_BaseColor", tint);
                    if (texture) m.SetTexture("_BaseMap", texture);
                    m.SetFloat("_Smoothness", character ? .14f : .36f);
                    m.SetFloat("_Metallic", character ? 0 : .18f);
                    if (character)
                    {
                        m.SetFloat("_Cull", 0);
                        m.doubleSidedGI = true;
                        m.EnableKeyword("_EMISSION");
                        if (texture) m.SetTexture("_EmissionMap", texture);
                        m.SetColor("_EmissionColor", tint * .13f);
                    }
                    AssetDatabase.CreateAsset(m, matPath);
                }
                if (character && folder.Contains("/Characters/")) CharacterShading.Apply(original.name,m);
                if (character) BindCharacterNormal(folder, original.name, m);
                var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + original.name + ".png");
                if(!character && original.name=="Ember47_T00")
                {
                    var deLit=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/Ember47Albedo.png");
                    if(deLit)diffuse=deLit;
                }
                if (diffuse && m.mainTexture != diffuse) { m.mainTexture = diffuse; EditorUtility.SetDirty(m); }
                if (!character && original.name.EndsWith("_T00", StringComparison.Ordinal))
                {
                    string normalPath = folder + "/" + original.name.Replace("_T00","_N") + ".png";
                    if (AssetImporter.GetAtPath(normalPath) is TextureImporter normalImport)
                    {
                        if (normalImport.textureType != TextureImporterType.NormalMap)
                        { normalImport.textureType = TextureImporterType.NormalMap; normalImport.SaveAndReimport(); }
                        m.SetTexture("_BumpMap", Load<Texture2D>(normalPath)); m.EnableKeyword("_NORMALMAP");
                        EditorUtility.SetDirty(m);
                    }
                }
                if(character && !StylizedCharacters.Editor.CharacterToonMaterial.IsPrepared(m) && m.HasProperty("_EmissionColor"))
                { m.SetColor("_EmissionColor",m.color*.035f);EditorUtility.SetDirty(m); }
                if (!character && (original.name.EndsWith("_Ruby") || original.name.EndsWith("_T00")))
                {
                    string metalPath=folder+"/"+original.name.Replace("_T00","_Metal")+".png";
                    var metal=original.name.EndsWith("_Ruby") ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(metalPath);
                    if (metal)
                    {
                        var imp=(TextureImporter)AssetImporter.GetAtPath(metalPath);
                        if(imp.sRGBTexture){imp.sRGBTexture=false;imp.SaveAndReimport();}
                        m.SetTexture("_MetallicGlossMap",metal);m.EnableKeyword("_METALLICSPECGLOSSMAP");m.SetFloat("_Smoothness",.82f);EditorUtility.SetDirty(m);
                    }
                    if(original.name.EndsWith("_Ruby")){m.SetFloat("_Metallic",.72f);m.SetFloat("_Smoothness",.76f);EditorUtility.SetDirty(m);}
                }
                if (original.name.StartsWith("Glow_"))
                {
                    m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", m.GetColor("_BaseColor")*1.45f);
                    EditorUtility.SetDirty(m);
                }
                if (existing.TryGetValue(id, out var current) && current == m) continue;
                importer.AddRemap(id,m); changes = true;
            }
            if (changes) importer.SaveAndReimport();
        }

        internal static void BindCharacterNormal(string folder, string name, Material material)
        {
            string path = folder + "/" + name + "_Normal.png";
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            if (importer.textureType != TextureImporterType.NormalMap || importer.sRGBTexture)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (material.shader.name.StartsWith("DanbaidongRP/PBRToon/", StringComparison.Ordinal))
            {
                if (!material.HasProperty("_NormalMap") || material.GetTexture("_NormalMap") == texture) return;
                material.SetTexture("_NormalMap", texture);
                material.SetFloat("_NormalScale", .35f);
                EditorUtility.SetDirty(material);
                return;
            }
            bool toon = StylizedCharacters.Editor.CharacterToonMaterial.IsPrepared(material);
            if (material.GetTexture("_BumpMap") == texture &&
                (toon ? material.GetFloat("_UseBumpMap")>.5f : material.IsKeywordEnabled("_NORMALMAP"))) return;
            material.SetTexture("_BumpMap",texture);
            material.SetFloat("_BumpScale",toon ? .35f : .65f);
            if(toon) material.SetFloat("_UseBumpMap",1);
            else material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
        }

        internal static T Definition<T>(ContentCatalog catalog, string kind, string file, string id, string label) where T : ContentDefinition
        {
            string path = $"{ContentLibrary.Root}/{kind}/{file}.asset";
            var item = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!item)
            {
                item = ScriptableObject.CreateInstance<T>();
                var data = new SerializedObject(item);
                data.FindProperty("_id").stringValue = id;
                data.FindProperty("_displayName").stringValue = label;
                data.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(item, path);
            }
            ContentLibrary.Register(catalog, item);
            return item;
        }

        [MenuItem("AnimeCS/美术/同步角色阵营布章")]
        public static void BakeTeamMarks()
        {
            foreach (var member in PlayableCast())
            {
                string name = member.name;
                var character = ContentLibrary.Catalog.Find<CharacterDefinition>("character."+name.ToLowerInvariant());
                if (!character) continue;
                var data = new SerializedObject(character);
                AssignTeamMarks(name, data);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }

        private static void AssignTeamMarks(string name, SerializedObject character)
        {
            const string folder = Art + "/Wearables/TeamMarks/";
            const string prefabFolder = "Assets/Content/Prefabs/Wearables/TeamMarks";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(folder + name + "TeamPatch.fbx");
            if (!model) return;
            ContentLibrary.EnsureFolder(prefabFolder);
            foreach (string side in new[] { "Attack", "Defend" })
            {
                string materialPath = folder + side + "Weave.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.SetFloat("_Smoothness", .18f);
                    material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                    material.doubleSidedGI = true;
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.mainTexture=Load<Texture2D>(folder + side + "Weave.png");
                EditorUtility.SetDirty(material);
                var root = new GameObject(name + " " + side + " insignia");
                try
                {
                    var item = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    foreach (var mesh in item.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        mesh.sharedMaterial = material;
                        mesh.updateWhenOffscreen = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(mesh);
                    }
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabFolder + "/" + name + side + ".prefab");
                    Set(character, side == "Attack" ? "_attackInsignia" : "_defendInsignia", prefab);
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("找不到美术资源 " + path);
        private static void Set(SerializedObject target, string field, Object value) => target.FindProperty(field).objectReferenceValue = value;

        private static AnimatorController CreateController(string donorPath)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(donorPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__")).ToDictionary(c => c.name);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(MotionPath);
            foreach (string param in new[] { "Strafe", "Forward", "Crouch", "Vertical", "AirTurn" })
                ac.AddParameter(param, AnimatorControllerParameterType.Float);
            var machine = ac.layers[0].stateMachine;
            BlendTree Tree(string name, string x, string y = null)
            {
                var tree = new BlendTree { name = name, useAutomaticThresholds = false, blendParameter = x,
                    blendType = y == null ? BlendTreeType.Simple1D : BlendTreeType.FreeformDirectional2D };
                if (y != null) tree.blendParameterY = y;
                AssetDatabase.AddObjectToAsset(tree, ac);
                return tree;
            }
            var upright = Tree("站立移动", "Strafe", "Forward");
            var duck = Tree("蹲姿移动", "Strafe", "Forward");
            upright.AddChild(clips["Idle"], Vector2.zero);
            duck.AddChild(clips["DuckIdle"], Vector2.zero);
            var axes = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            var directions = new[] { "Forward", "Back", "Left", "Right" };
            for (int i = 0; i < axes.Length; i++)
            {
                upright.AddChild(clips["Walk" + directions[i]], axes[i] * .51f);
                upright.AddChild(clips["Run" + directions[i]], axes[i]);
                duck.AddChild(clips["Duck" + directions[i]], axes[i] * .34f);
            }
            var ground = Tree("站蹲过渡", "Crouch");
            ground.AddChild(upright, 0); ground.AddChild(duck, 1);
            var airborne = Tree("起跳与下落", "Vertical");
            airborne.AddChild(clips["Fall"], -1); airborne.AddChild(clips["Rise"], 1);
            var turning = Tree("空中侧移", "AirTurn");
            turning.AddChild(clips["AirLeft"], -1); turning.AddChild(airborne, 0); turning.AddChild(clips["AirRight"], 1);
            machine.defaultState = machine.AddState("Ground");
            machine.defaultState.motion = ground;
            machine.AddState("Air").motion = turning;
            machine.AddState("Land").motion = clips["Land"];
            EditorUtility.SetDirty(ac);
            return ac;
        }
    }
}
