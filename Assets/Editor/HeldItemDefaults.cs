using System;
using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    public static class HeldItemDefaults
    {
        public const string Path = "Assets/Content/Data/HeldItemLibrary.asset";
        private const string Audio = "Assets/Content/Audio/Courtyard/";

        // Fill missing items only so designers can regenerate after extending the catalog.
        [MenuItem("AnimeCS/战斗/补齐持物动作与音效")]
        public static void Build()
        {
            var library = AssetDatabase.LoadAssetAtPath<HeldItemLibrary>(Path);
            if (!library)
            {
                library = ScriptableObject.CreateInstance<HeldItemLibrary>();
                AssetDatabase.CreateAsset(library, Path);
            }
            foreach (var item in ContentLibrary.Catalog.Entries)
            {
                if (!item || item.Archived || library.items.Any(row => row.itemId == item.Id)) continue;
                if (item is WeaponDefinition weapon)
                    library.items.Add(Weapon(weapon));
                else if (item is UtilityDefinition utility)
                    library.items.Add(Utility(utility.Id, utility.Effect));
            }
            if (!library.items.Any(row => row.itemId == HeldItemLibrary.ObjectiveId))
                library.items.Add(new HeldItemActions
                {
                    itemId = HeldItemLibrary.ObjectiveId,
                    inspect = Inspect(2.28f, new(-.006f,.026f,.014f), new(-18,5,-7), new(-8,-16,9)),
                    inspectSound = Cue("inspect-kit"), activateSound = Cue("objective")
                });
            if (!library.devicePulse) library.devicePulse = Cue("objective-pulse");
            if (!library.deviceBurst) library.deviceBurst = Cue("objective-release");
            EditorUtility.SetDirty(library);
            foreach (string guid in AssetDatabase.FindAssets("t:MatchPresentation", new[] { "Assets/Content" }))
            {
                var preset = AssetDatabase.LoadAssetAtPath<MatchPresentation>(AssetDatabase.GUIDToAssetPath(guid));
                if (!preset || preset.heldItems) continue;
                preset.heldItems = library;
                EditorUtility.SetDirty(preset);
            }
            AssetDatabase.SaveAssets();
        }

        private static AudioClip Cue(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Audio + name + ".wav");
        private static string Name(string id) => id.Substring(id.LastIndexOf('.') + 1);

        private static HeldItemActions Weapon(WeaponDefinition gun)
        {
            string name = Name(gun.Id);
            bool blade = gun.Class == WeaponClass.Knife;
            bool precision = gun.Class is WeaponClass.LightSniper or WeaponClass.HeavySniper;
            bool pistol = gun.Class == WeaponClass.Pistol;
            int accent = name.Aggregate(0, (n,c) => (n * 31 + c) % 13) - 6;
            Vector3 show = gun.Inspect switch
            {
                InspectStyle.Sidearm => new(-18,-33,-38),
                InspectStyle.Precision => new(-7,-25,-19),
                InspectStyle.StraightBlade => new(-16,28,-55),
                InspectStyle.RingBlade => new(-21,36,-85),
                InspectStyle.FoldingBlade => new(14,34,-64),
                _ => gun.Class == WeaponClass.Smg ? new(-18,-25,-32) : new(-10,-36,-27)
            };
            show.z += accent;
            var turn = new Vector3(show.x*.35f, show.y*.62f, -show.z*.6f);
            Vector3 offset = blade ? new(-.036f,.042f,.02f) :
                precision ? new(-.043f,.032f,.023f) : new(-.063f,.045f,.032f);
            var row = new HeldItemActions
            {
                itemId = gun.Id,
                inspect = Inspect((blade ? 1.98f : pistol ? 2.17f : 2.5f) + .025f * accent, offset, show, turn,
                    gun.Inspect == InspectStyle.FoldingBlade ? 100 : 0),
                inspectSound = Cue("inspect-" + (blade ? "edge" : precision ? "glass" : pistol ? "side" : "long")),
                activateSound = Cue(name)
            };
            if (!blade) return row;
            row.light = Strike(.32f, new(.018f,.007f,-.016f), new(-.068f,.025f,.047f),
                new(-4,15,-12), new(-9,-32,34));
            row.heavy = Strike(.61f, new(.044f,-.023f,-.051f), new(-.016f,.012f,.117f),
                new(4,21,-25), new(-11,-9,11));
            row.rear = Strike(.72f, new(.038f,.025f,-.035f), new(-.030f,-.018f,.091f),
                new(-13,18,-38), new(7,-23,27));
            row.heavySound = Cue("edge-heavy");
            row.rearSound = Cue("edge-rear");
            return row;
        }

        private static HeldItemActions Utility(string id, UtilityEffect effect)
        {
            var show = effect switch
            {
                UtilityEffect.Flash => new Vector3(-22,31,-14),
                UtilityEffect.Smoke => new Vector3(-8,22,-38),
                _ => new Vector3(-18,-29,21)
            };
            return new HeldItemActions
            {
                itemId = id, inspectSound = Cue("inspect-kit"), activateSound = Cue(Name(id)),
                inspect = Inspect(1.88f, new(-.031f,.032f,.026f), show, new(-6,-show.y*.8f,show.z*.45f))
            };
        }

        private static HandlingTrack Inspect(float duration, Vector3 offset, Vector3 show, Vector3 reverse, float hinge = 0) =>
            new() { seconds = duration, keys = new[] {
                new HandlingKey(0,default,default), new HandlingKey(.22f,offset,show,hinge),
                new HandlingKey(.40f,offset*1.03f,show*1.03f,hinge*.4f),
                new HandlingKey(.65f,offset*.83f,reverse), new HandlingKey(.79f,offset*.83f,reverse),
                new HandlingKey(1,default,default)
            } };

        private static HandlingTrack Strike(float duration, Vector3 lift, Vector3 travel, Vector3 wind, Vector3 follow) =>
            new() { seconds = duration, keys = new[] {
                new HandlingKey(0,default,default), new HandlingKey(.15f,lift,wind),
                new HandlingKey(.34f,travel,follow), new HandlingKey(.50f,travel*.9f,follow*.82f),
                new HandlingKey(1,default,default)
            } };
    }
}
