using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace AnimeCS.Editor
{
    internal sealed class PlayableCatalogBuild : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var game in root.GetComponentsInChildren<LocalGame>(true))
            {
                var owner = new SerializedObject(game);
                var slot = owner.FindProperty("_catalog");
                if (slot.objectReferenceValue is not ContentCatalog catalog) continue;
                slot.objectReferenceValue = catalog.ForPlayerBuild();
                owner.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
