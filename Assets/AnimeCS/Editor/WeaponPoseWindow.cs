using System.Linq;
using AnimeCS.Content;
using AnimeCS.Play;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UIElements;

namespace AnimeCS.Editor
{
    public sealed class WeaponPoseWindow : EditorWindow
    {
        [SerializeField] private WeaponDefinition _weapon;
        [SerializeField] private OutfitDefinition _outfit;
        private bool _view,_duck;
        private float _pitch,_yaw;
        private CharacterRig _rig;
        private float _angle=62, _distance=1.45f;
        private PreviewRenderUtility _preview;
        private PlayableGraph _graph;
        private Transform _actor, _mount;
        private WeaponGripRig _hands;
        private IMGUIContainer _surface;

        public static void Open(WeaponDefinition gun)
        {
            var window=GetWindow<WeaponPoseWindow>("持枪校准");
            window._weapon=gun;
            if (!window._outfit) window._outfit=ContentLibrary.Catalog.Outfit;
            window.CreateGUI(); window.Show();
        }

        public void CreateGUI()
        {
            DisposePreview();
            var root=rootVisualElement; root.Clear();
            var items=new VisualElement(); items.style.flexDirection=FlexDirection.Row; root.Add(items);
            var gun=new ObjectField("枪械"){objectType=typeof(WeaponDefinition),value=_weapon}; gun.style.flexGrow=1;items.Add(gun);
            var outfit=new ObjectField("角色服装"){objectType=typeof(OutfitDefinition),value=_outfit};outfit.style.flexGrow=1;items.Add(outfit);
            gun.RegisterValueChangedCallback(e=>{_weapon=e.newValue as WeaponDefinition;CreateGUI();});
            outfit.RegisterValueChangedCallback(e=>{_outfit=e.newValue as OutfitDefinition;CreateGUI();});
            var view=new Toggle("第一人称"){value=_view};root.Add(view);
            view.RegisterValueChangedCallback(e=>{_view=e.newValue;CreateGUI();});
            if (!_weapon || !_outfit || !_outfit.Character) return;
            if(!_view)
            {
                var poses=new VisualElement();poses.style.flexDirection=FlexDirection.Row;root.Add(poses);
                var duck=new Toggle("蹲姿"){value=_duck};poses.Add(duck);duck.RegisterValueChangedCallback(e=>{_duck=e.newValue;CreateGUI();});
                var pitch=new Slider("抬头 / 低头",-82,82){value=_pitch,showInputField=true};root.Add(pitch);
                var yaw=new Slider("上身左右转向",-55,55){value=_yaw,showInputField=true};root.Add(yaw);
                pitch.RegisterValueChangedCallback(e=>{_pitch=e.newValue;Repaint();});yaw.RegisterValueChangedCallback(e=>{_yaw=e.newValue;Repaint();});
            }
            var data=new SerializedObject(_weapon);
            foreach (var field in new[]{("_grip","右手接触点"),("_support","左手接触点"),
                ("_shoulderPad","靠肩点"),("_uprightSupport","左手环握前端"),
                (_view?"_viewOffset":"_worldOffset","模型偏移"),(_view?"_viewAngles":"_worldAngles","模型旋转")})
                root.Add(new PropertyField(data.FindProperty(field.Item1),field.Item2));
            if(_weapon.Class!=WeaponClass.Knife)
            {
                string fieldName=_view?"_viewMuzzle":"_muzzle";
                var tipField=new PropertyField(data.FindProperty(fieldName),_view?"第一人称枪口覆盖":"枪口中心");
                tipField.RegisterCallback<SerializedPropertyChangeEvent>(_=>Repaint());root.Add(tipField);
                var marker=ContentLibrary.MuzzleMarker(_weapon.PosedModel(_view));
                if(marker!=Vector3.zero)root.Add(new Button(()=>{
                    data.FindProperty(fieldName).vector3Value=marker;data.ApplyModifiedProperties();
                    AssetDatabase.SaveAssetIfDirty(_weapon);Repaint();
                }){text="读取模型的 Muzzle 节点"});
            }
            root.Bind(data);
            _surface=new IMGUIContainer(DrawPreview);
            _surface.style.flexGrow=1; _surface.style.minHeight=180;root.Add(_surface);
            BuildPreview();
        }

        private void BuildPreview()
        {
            _preview=new PreviewRenderUtility();
            _preview.ambientColor=new Color(.70f,.73f,.79f);
            _preview.lights[0].intensity=1.6f;_preview.lights[0].transform.rotation=Quaternion.Euler(28,145,0);
            _preview.lights[1].intensity=1f;_preview.lights[1].transform.rotation=Quaternion.Euler(12,-34,0);
            var stage=new GameObject("持枪预览"); _preview.AddSingleGO(stage); _actor=stage.transform;
            var model=_view ? _outfit.ViewRig : _outfit.Character.Model;
            if (_view && !model) model=_outfit.ViewArms(_weapon.Class==WeaponClass.Pistol);
            var body=AppearancePresenter.AddVisual(model,_actor,_view && _outfit.ViewRig ? Vector3.down*1.60f : Vector3.zero);
            var rig=_rig=new CharacterRig(body,_outfit.Character,_view ? null : _outfit.PoseController); _hands=rig.Grip;
            if (!_view) rig.FitDrape(_duck ? 1 : 0);
            if (!_view) rig.BindClothing(AppearancePresenter.AddVisual(_outfit.Model,_actor,Vector3.zero));
            var animator=body ? body.GetComponentInChildren<Animator>() : null;
            var poseController=_view ? _outfit.Character.Controller : _outfit.PoseController;
            if (animator && animator.isHuman && poseController)
            {
                var clip=poseController.animationClips.FirstOrDefault(c=>c.name==(_duck && !_view ? "DuckIdle" : "Idle"));
                if (clip)
                {
                    _graph=PlayableGraph.Create("持枪待机"); _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output=AnimationPlayableOutput.Create(_graph,"Idle",animator);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(_graph,clip));_graph.Play();_graph.Evaluate(.15f);
                }
            }
            var parent=(_hands?.Valid!=true && rig.WeaponSocket && !_view) ? rig.WeaponSocket : _actor;
            _mount=AppearancePresenter.MountGun(parent,_view ? new Vector3(.23f,-.22f,.43f) : Vector3.zero,_weapon,null,_view).transform;
        }

        private void DrawPreview()
        {
            if (_preview==null || !_weapon) return;
            var rect=_surface.contentRect;
            if (rect.width<2 || rect.height<2) return;
            rect.position=Vector2.zero;
            var e=Event.current;
            if (!_view && e.type==EventType.MouseDrag && e.button==0)
            { _angle+=e.delta.x*.65f; e.Use(); Repaint(); }
            if (!_view && e.type==EventType.ScrollWheel)
            { _distance=Mathf.Clamp(_distance+e.delta.y*.06f,.42f,3.8f);e.Use();Repaint(); }
            if (e.type!=EventType.Repaint) return;
            _rig?.RestoreHitPose();if (_graph.IsValid()) _graph.Evaluate(0);
            if(!_view)_rig?.Look(_actor,_yaw,_pitch);
            var aim=_view ? Quaternion.identity : Quaternion.Euler(-Mathf.Clamp(_pitch,-66,69),_yaw,0);
            _hands?.Pose(_actor,_mount,_weapon,aim,_view);
            var camera=_preview.camera;
            camera.nearClipPlane=.025f;camera.farClipPlane=18;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.22f,.24f);
            if (_view)
            { camera.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);camera.fieldOfView=74; }
            else
            {
                var focus=new Vector3(0,_duck?.88f:1.25f,.17f);
                var eye=focus+Quaternion.Euler(0,_angle,0)*new Vector3(0,.135f,_distance);
                camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(focus-eye));camera.fieldOfView=36;
            }
            _preview.BeginPreview(rect,GUIStyle.none);_preview.Render(true);
            GUI.DrawTexture(rect,_preview.EndPreview(),ScaleMode.StretchToFill,false);
            if (_weapon.Class!=WeaponClass.Knife && _weapon.Muzzle(_view)!=Vector3.zero)
            {
                var spot=camera.WorldToViewportPoint(_mount.TransformPoint(_weapon.Muzzle(_view)));
                var center=new Vector2(rect.x+spot.x*rect.width,rect.y+(1-spot.y)*rect.height);
                if (spot.z>0 && rect.Contains(center))
                {
                    var color=new Color(1f,.77f,.27f);
                    EditorGUI.DrawRect(new Rect(center.x-6,center.y-.6f,12,1.2f),color);
                    EditorGUI.DrawRect(new Rect(center.x-.6f,center.y-6,1.2f,12),color);
                }
            }
        }

        private void OnDisable() => DisposePreview();
        private void DisposePreview()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _preview?.Cleanup();_preview=null;_hands=null;_actor=null;_mount=null;
        }
    }
}
