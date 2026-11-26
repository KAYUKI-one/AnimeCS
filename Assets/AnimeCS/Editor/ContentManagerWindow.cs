using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AnimeCS.Content;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimeCS.Editor
{
    public sealed class ContentManagerWindow : EditorWindow
    {
        [SerializeField] private ContentKind _kind;
        [SerializeField] private ContentDefinition _selected;
        [SerializeField] private string _search = "";
        private ContentCatalog _catalog;
        private ListView _list;
        private readonly List<ContentDefinition> _shown = new List<ContentDefinition>();
        private readonly List<Button> _tabs = new List<Button>();
        private ScrollView _details;
        private Label _status;
        private bool _handles, _refreshing;

        [MenuItem("AnimeCS/资源管理器", priority = 1)]
        public static void Open() => GetWindow<ContentManagerWindow>("AnimeCS 资源管理器").Show();

        private void OnEnable()
        {
            minSize = new Vector2(660, 410);
            Undo.undoRedoPerformed += Refresh;
            SceneView.duringSceneGui += DrawMapHandles;
            EditorApplication.projectChanged += Refresh;
        }
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            SceneView.duringSceneGui -= DrawMapHandles;
            EditorApplication.projectChanged -= Refresh;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/AnimeCS/Editor/ContentManager.uss");
            if (sheet != null) root.styleSheets.Add(sheet);
            var top = Row(root, "top");
            var title = new Label("项目资源"); title.AddToClassList("title"); top.Add(title);
            AddButton(top, "校验目录", () => Run(() =>
            {
                var results = FoundationBuild.ValidateCatalog(_catalog).ToArray();
                Status(results.Length == 0 ? $"通过校验 · {_catalog.Entries.Count} 个条目" : string.Join("\n", results), results.Length > 0);
            }));
            AddButton(top, "目录设置", () => Selection.activeObject = _catalog);
            var tabs = Row(root, "tabs"); _tabs.Clear();
            foreach (ContentKind kind in Enum.GetValues(typeof(ContentKind)))
                _tabs.Add(AddButton(tabs, ContentLibrary.Labels[(int)kind], () =>
                {
                    _kind = kind; _selected = null; _handles = false; Refresh();
                    Status(""); SceneView.RepaintAll();
                }));

            var split = new TwoPaneSplitView(0, 255, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1; root.Add(split);
            var left = new VisualElement(); left.AddToClassList("rail"); split.Add(left);
            var search = new ToolbarSearchField { name = "search" }; search.SetValueWithoutNotify(_search);
            search.tooltip = "按名称或稳定 ID 搜索当前分类";
            search.RegisterValueChangedCallback(e => { _search = e.newValue; Refresh(); }); left.Add(search);
            _list = new ListView(_shown, 30, () => new Label(), (row, i) => ((Label)row).text = _shown[i].DisplayName);
            _list.name = "entries"; _list.style.flexGrow = 1; _list.selectionType = SelectionType.Single;
            _list.selectionChanged += values =>
            {
                if (_refreshing) return;
                _selected = values.OfType<ContentDefinition>().FirstOrDefault();
                _handles = false; DrawDetails(); SceneView.RepaintAll();
            };
            left.Add(_list);
            var actions = Row(left, "actions");
            AddButton(actions, "新建条目", () => Run(() => SelectEntry(ContentLibrary.Create(_catalog, _kind, "新" + ContentLibrary.Labels[(int)_kind]))));
            AddButton(actions, "从磁盘导入", () => PickFile(false));
            var drop = new Label("拖入素材以登记 · 支持多选"); drop.AddToClassList("drop"); left.Add(drop);
            DropZone(drop, false);
            _details = new ScrollView { name = "details" }; _details.AddToClassList("details"); split.Add(_details);
            _status = new Label { name = "status" }; _status.AddToClassList("status"); root.Add(_status);
            Refresh();
        }

        private void Refresh()
        {
            if (_list == null || _details == null) return;
            _catalog = ContentLibrary.Catalog;
            _refreshing = true;
            try
            {
                _shown.Clear();
                if (_catalog != null)
                    _shown.AddRange(_catalog.Entries.Where(e => e != null && e.Kind == _kind &&
                        (e.DisplayName.IndexOf(_search ?? "", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (e.Id ?? "").IndexOf(_search ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
                        .OrderBy(e => e.DisplayName, StringComparer.CurrentCulture));
                if (!_shown.Contains(_selected)) _selected = _shown.FirstOrDefault();
                _list.Rebuild();
                int index = _shown.IndexOf(_selected);
                _list.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
                for (int i = 0; i < _tabs.Count; i++) _tabs[i].EnableInClassList("active", i == (int)_kind);
                DrawDetails();
            }
            finally { _refreshing = false; }
        }

        private void SelectEntry(ContentDefinition entry)
        {
            _selected = entry;
            if (!string.IsNullOrEmpty(_search))
            {
                _search = "";
                rootVisualElement.Q<ToolbarSearchField>("search").SetValueWithoutNotify("");
            }
            Refresh();
            int index = _shown.IndexOf(entry);
            if (index >= 0) _list.ScrollToItem(index);
        }

        private void DrawDetails()
        {
            _details.Unbind(); _details.Clear();
            if (_selected == null) { _details.Add(new Label("当前没有匹配的条目。")); return; }
            var entry = _selected;
            var header = Row(_details, "asset-heading");
            var title = new Label(entry.DisplayName); title.AddToClassList("title"); header.Add(title);
            Object preview = entry switch
            {
                MapDefinition m => m.Geometry, CharacterDefinition c => c.Model, OutfitDefinition o => o.Model != null ? o.Model : o.Character?.Model,
                WeaponDefinition w => w.WorldModel, WeaponSkinDefinition s => s.Material != null ? (Object)s.Material : s.Texture,
                UtilityDefinition u => u.Model, WearableDefinition wear => wear.Model(false) ? (Object)wear.Model(false) : wear.Material, _ => null
            };
            if (preview != null)
            {
                var image = new Image { scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("preview"); header.Add(image);
                image.image = AssetPreview.GetAssetPreview(preview);
                if (image.image == null)
                {
                    image.image = AssetPreview.GetMiniThumbnail(preview);
                    image.schedule.Execute(() =>
                    {
                        var texture = AssetPreview.GetAssetPreview(preview);
                        if (texture != null) image.image = texture;
                    }).Every(180).ForDuration(2500);
                }
            }
            var id = new TextField("稳定 ID") { value = entry.Id, isReadOnly = true }; _details.Add(id);
            var actions = Row(_details, "actions");
            AddButton(actions, "定位条目", () => EditorGUIUtility.PingObject(entry));
            if (entry is WeaponDefinition weapon) AddButton(actions,"预览与校准持枪",()=>WeaponPoseWindow.Open(weapon));
            AddButton(actions, "移出目录", () => Run(() =>
            {
                ContentLibrary.Unregister(_catalog, entry); Refresh(); Status("条目已移出目录，源文件仍在 Project 中。");
            }));
            if (entry is MapDefinition map)
            {
                AddButton(actions, "打开地图素材", () => Run(() =>
                {
                    Object source = !string.IsNullOrEmpty(map.ScenePath) ? AssetDatabase.LoadAssetAtPath<SceneAsset>(map.ScenePath) : map.Geometry;
                    if (source == null) throw new InvalidOperationException("请先为地图绑定场景或模型。");
                    AssetDatabase.OpenAsset(source);
                }));
                AddButton(actions, _catalog.Map == map ? "当前启动地图" : "设为启动地图",
                    () => Run(() => { FoundationBuild.SetMap(_catalog, map); Refresh(); Status("已设置启动地图。"); }));
                var handles = new Toggle("场景点位编辑") { value = _handles };
                handles.RegisterValueChangedCallback(e => { _handles = e.newValue; SceneView.RepaintAll(); });
                _details.Add(handles);
                if (!string.IsNullOrEmpty(map.ScenePath)) _details.Add(new Label("场景 · " + map.ScenePath));
            }
            var bind = Row(_details, "bind");
            var picker = new ObjectField { name = "source", allowSceneObjects = false, objectType = typeof(Object) };
            picker.style.flexGrow = 1; bind.Add(picker);
            AddButton(bind, "绑定素材", () => Run(() => { ContentLibrary.Bind(entry, picker.value); Refresh(); Status("已绑定素材。"); }));
            AddButton(bind, "选择磁盘文件", () => PickFile(true));
            var hint = new Label("拖到这里替换当前条目的素材"); hint.AddToClassList("drop"); _details.Add(hint);
            DropZone(hint, true);

            var data = new SerializedObject(entry);
            var property = data.GetIterator();
            bool children = true;
            while (property.NextVisible(children))
            {
                children = false;
                if (property.propertyPath == "m_Script") continue;
                string display = property.displayName;
                for (var type = entry.GetType(); type != null && type != typeof(ScriptableObject); type = type.BaseType)
                {
                    var member = type.GetField(property.name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    var label = member?.GetCustomAttribute<InspectorNameAttribute>();
                    if (label != null) { display = label.displayName; break; }
                }
                var field = new PropertyField(property.Copy(), display); _details.Add(field);
                if (property.propertyPath == "_displayName")
                    field.RegisterValueChangeCallback(e => { title.text = entry.DisplayName; _list.RefreshItems(); });
            }
            _details.Bind(data);
        }

        private void PickFile(bool bind)
        {
            string path = EditorUtility.OpenFilePanel(bind ? "绑定到当前条目" : "导入并登记新素材", "", "");
            if (!string.IsNullOrEmpty(path)) Run(() => ImportOne(ContentLibrary.ImportFile(_kind, path), bind));
        }

        private void DropZone(VisualElement target, bool bind)
        {
            target.RegisterCallback<DragUpdatedEvent>(e => { DragAndDrop.visualMode = DragAndDropVisualMode.Copy; e.StopPropagation(); });
            target.RegisterCallback<DragPerformEvent>(e =>
            {
                DragAndDrop.AcceptDrag(); e.StopPropagation();
                var objects = DragAndDrop.objectReferences.Where(o => o != null).ToArray();
                var paths = DragAndDrop.paths.Where(File.Exists).ToArray();
                if (bind && Math.Max(objects.Length, paths.Length) > 1) { Status("绑定时请只拖入一个素材。", true); return; }
                var errors = new List<string>(); int count = 0;
                void Process(Func<Object> load)
                {
                    try { ImportOne(load(), bind); count++; } catch (Exception ex) { errors.Add(ex.Message); }
                }
                if (objects.Length > 0) foreach (var obj in objects) Process(() => obj);
                else foreach (string path in paths) Process(() => ContentLibrary.ImportFile(_kind, path));
                Status($"已处理 {count} 项" + (errors.Count > 0 ? "\n" + string.Join("\n", errors) : ""), errors.Count > 0);
            });
        }

        private void ImportOne(Object source, bool bind)
        {
            if (bind) { ContentLibrary.Bind(_selected, source); Refresh(); }
            else SelectEntry(ContentLibrary.Import(_catalog, _kind, source));
            Status("已保存 · " + _selected.DisplayName);
        }

        private void DrawMapHandles(SceneView view)
        {
            if (!_handles || !(_selected is MapDefinition map) || EditorApplication.isPlaying) return;
            var data = new SerializedObject(map);
            void Move(SerializedProperty prop, string label, Color color)
            {
                Handles.color = color;
                Vector3 position = prop.vector3Value;
                Handles.Label(position + Vector3.up * 1.1f, label);
                EditorGUI.BeginChangeCheck();
                var result = Handles.PositionHandle(position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck()) prop.vector3Value = result;
            }
            foreach (Side side in Enum.GetValues(typeof(Side)))
            {
                var array = data.FindProperty(side == Side.Attack ? "_attackSpawns" : "_defendSpawns");
                for (int i = 0; i < array.arraySize; i++)
                    Move(array.GetArrayElementAtIndex(i), (side == Side.Attack ? "攻 " : "守 ") + (i + 1), Color.white);
            }
            Move(data.FindProperty("_siteA"), "展台 A", Color.cyan);
            Move(data.FindProperty("_siteB"), "录音棚 B", Color.yellow);
            var regions = data.FindProperty("_regions");
            for (int i = 0; i < regions.arraySize; i++)
            {
                var region = regions.GetArrayElementAtIndex(i);
                Move(region.FindPropertyRelative("Center"), region.FindPropertyRelative("Name").stringValue, Color.green);
            }
            data.ApplyModifiedProperties();
        }

        private static VisualElement Row(VisualElement parent, string css)
        {
            var row = new VisualElement(); row.AddToClassList(css); parent.Add(row); return row;
        }
        private static Button AddButton(VisualElement parent, string label, Action click)
        {
            var button = new Button(click) { text = label }; parent.Add(button); return button;
        }
        private void Status(string message, bool error = false)
        {
            _status.text = message; _status.EnableInClassList("error", error);
        }
        private void Run(Action action)
        {
            try { action(); } catch (Exception ex) { Status(ex.Message, true); }
        }
    }
}
