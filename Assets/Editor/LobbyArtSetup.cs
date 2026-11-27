using AnimeCS.Play;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.EditorTools
{
    public static class LobbyArtSetup
    {
        const string Art = "Assets/Content/Art/Lobby/";
        [MenuItem("AnimeCS/大厅/创建或修复默认美术引用")]
        public static void Prepare()
        {
            var presentation=AssetDatabase.LoadAssetAtPath<MatchPresentation>("Assets/Content/UI/Competitive.asset");
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Art+"LobbyBackdrop.shader");
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"Courtyard.png");
            if (!presentation || !shader || !texture) throw new System.InvalidOperationException("大厅默认美术尚未导入");
            Material MaterialAt(string name,bool shadow)
            {
                string path=Art+name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material) return material;
                material=new Material(shader){name=name};
                if (shadow)
                {
                    material.SetFloat("_Contact",1);
                    material.SetColor("_Color",new Color(.10f,.20f,.25f,.36f));
                }
                else {material.mainTexture=texture; material.renderQueue=2501;}
                AssetDatabase.CreateAsset(material,path);
                return material;
            }
            if (!presentation.lobbyTheme) presentation.lobbyTheme=AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/Content/UI/Lobby.uss");
            if (!presentation.lobbyBackdrop) presentation.lobbyBackdrop=MaterialAt("Courtyard",false);
            if (!presentation.lobbyContactShadow) presentation.lobbyContactShadow=MaterialAt("Contact Shadow",true);
            EditorUtility.SetDirty(presentation);
            AssetDatabase.SaveAssets();
        }
    }
}