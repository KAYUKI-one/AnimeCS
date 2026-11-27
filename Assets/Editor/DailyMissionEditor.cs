using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AnimeCS.Editor
{
    internal sealed class DailyMissionEditor : EditorWindow
    {
        private static readonly string Catalog = Path.GetFullPath("Servers/Data/game-catalog.json");
        private static readonly string[] Events = { "login", "practice.enter", "match.enter", "round.complete" };
        private static readonly string[] EventNames = { "登录", "进入练枪场", "进入爆破对局", "完成爆破回合" };
        private JArray _missions;
        private string _original, _status;
        private Vector2 _scroll;

        [MenuItem("AnimeCS/任务/每日任务编辑器")]
        internal static void Open() => GetWindow<DailyMissionEditor>("每日任务");

        private void OnEnable()
        {
            try { _original = ReadTasks().ToString(); _missions = JArray.Parse(_original); }
            catch (Exception e) { _status = e.Message; }
        }

        private static JArray ReadTasks() => (JArray)JObject.Parse(File.ReadAllText(Catalog))["tasks"];

        private void OnGUI()
        {
            if (_missions != null)
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                int remove = -1;
                for (int i = 0; i < _missions.Count; i++)
                {
                    var row = (JObject)_missions[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    row["id"] = EditorGUILayout.TextField("任务 ID", (string)row["id"]);
                    row["title"] = EditorGUILayout.TextField("标题", (string)row["title"]);
                    row["description"] = EditorGUILayout.TextField("说明", (string)row["description"]);
                    int index = EditorGUILayout.Popup("进度事件", Array.IndexOf(Events, (string)row["eventKey"]), EventNames);
                    if (index >= 0) row["eventKey"] = Events[index];
                    row["target"] = Mathf.Max(1, EditorGUILayout.IntField("目标次数", (int)row["target"]));
                    row["experience"] = Mathf.Max(0, EditorGUILayout.IntField("经验奖励", (int)row["experience"]));
                    row["credits"] = Mathf.Max(0, EditorGUILayout.IntField("星币奖励", (int)row["credits"]));
                    if (GUILayout.Button("移除此任务")) remove = i;
                    EditorGUILayout.EndVertical();
                }
                if (remove >= 0) _missions.RemoveAt(remove);
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("添加每日任务"))
                    _missions.Add(new JObject { ["id"]="daily."+Guid.NewGuid().ToString("N").Substring(0,8),
                        ["title"]="新任务", ["description"]="", ["eventKey"]=Events[0], ["target"]=1, ["experience"]=0, ["credits"]=0 });
                if (GUILayout.Button("保存到数据服务配置")) Save();
            }
            if (GUILayout.Button("重新读取配置")) OnEnable();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void Save()
        {
            try
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var task in _missions)
                    if (string.IsNullOrWhiteSpace((string)task["id"]) || !ids.Add((string)task["id"]) ||
                        string.IsNullOrWhiteSpace((string)task["title"]) || !Events.Contains((string)task["eventKey"]))
                        throw new InvalidDataException("请填写唯一 ID、任务标题和有效事件。");
                var catalog = JObject.Parse(File.ReadAllText(Catalog));
                if (catalog["tasks"].ToString() != _original) throw new IOException("任务已在外部修改，请重新读取后编辑。");
                catalog["tasks"] = _missions.DeepClone();
                var temp = Catalog + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temp, catalog.ToString(Formatting.Indented) + Environment.NewLine); File.Replace(temp, Catalog, null); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                _original = _missions.ToString();
                _status = "已保存。数据服务重启后读取新任务。";
            }
            catch (Exception e) { _status = e.Message; }
        }
    }
}
