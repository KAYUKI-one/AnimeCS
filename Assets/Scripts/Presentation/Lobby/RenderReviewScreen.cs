using System;
using System.Collections.Generic;
using System.Linq;
using AnimeCS.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class RenderReviewScreen : IDisposable
    {
        private readonly VisualElement _view, _viewport;
        private readonly RenderReviewStage _stage;
        private readonly Label _name, _resolution;
        private readonly List<Button> _cards = new();
        private int _sampling = 2;

        internal RenderReviewScreen(VisualElement parent, ContentCatalog catalog, MatchPresentation style, Action close)
        {
            _stage = new RenderReviewStage(style);
            _view = new VisualElement { name = "render-review-screen" };
            _view.AddToClassList("render-review");
            if (style.lobbyTheme) _view.styleSheets.Add(style.lobbyTheme);
            var header = Add(_view, "review-header");
            Button(header, "‹  返回大厅", "review-back", close);
            Label(header, "渲染查看", "review-title");
            _resolution = Label(header, "", "review-resolution");
            _resolution.name = "review-resolution";

            var work = Add(_view, "review-work");
            _viewport = Add(work, "review-viewport");
            _viewport.name = "review-viewport";
            _name = Label(_viewport, "", "review-character");
            _name.name = "review-character";
            Label(_viewport, "拖动环绕  ·  滚轮缩放", "review-hint");
            BindOrbit();
            _viewport.RegisterCallback<GeometryChangedEvent>(_ => Resize());

            var controls = Add(work, "review-controls");
            var motion = Group(controls, "动作");
            Choices(motion, "review-motion", new[] { "站立", "跑动", "下蹲", "漫步", "展示" }, i => _stage.Action = (RenderReviewStage.Motion)i);
            var actions = Add(motion, "review-row");
            Button(actions, "转身", "review-turn", _stage.Turn);
            var pause = Button(actions, "暂停动画", "review-pause");
            pause.clicked += () =>
            {
                _stage.Paused = !_stage.Paused;
                pause.text = _stage.Paused ? "继续动画" : "暂停动画";
            };

            var lights = Group(controls, "灯光");
            Choices(lights, "review-light", new[] { "棚拍", "日光", "逆光" }, i => _stage.SetLighting((RenderReviewStage.Lighting)i));
            var camera = Add(Group(controls, "镜头"), "review-row");
            Button(camera, "全身", "review-full", () => _stage.Frame(false));
            Button(camera, "特写", "review-closeup", () => _stage.Frame(true));
            Choices(Group(controls, "抗锯齿"), "review-samples", new[] { "2× 采样", "1× 采样" }, i =>
            {
                _sampling = 2 - i;
                Resize();
            });

            var roster = new ScrollView(ScrollViewMode.Horizontal) { name = "review-roster" };
            roster.AddToClassList("review-roster");
            roster.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _view.Add(roster);
            var outfits = catalog.SeatOutfits.Where(o => o && !o.Archived && o.Character && !o.Character.Archived && o.Character.Model)
                .GroupBy(o => o.Character).Select(group => group.First()).ToArray();
            int initial = Array.FindIndex(outfits, outfit => outfit.Character.Id == "character.firefly");
            for (int i = 0; i < outfits.Length; i++)
            {
                int index = i;
                var character = outfits[i].Character;
                var card = Button(roster, "", "review-" + character.Id, () => Select(outfits[index], index));
                card.AddToClassList("review-card");
                card.Add(new Image { image = character.Icon ? character.Icon : character.Portrait, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore });
                Label(card, character.DisplayName, "review-card-name");
                _cards.Add(card);
            }
            if (outfits.Length != 0) Select(outfits[Mathf.Max(0, initial)], Mathf.Max(0, initial));
            parent.Add(_view);
        }

        private void Select(OutfitDefinition outfit, int index)
        {
            _stage.Select(outfit);
            _name.text = outfit.Character.DisplayName;
            for (int i = 0; i < _cards.Count; i++) _cards[i].EnableInClassList("selected", i == index);
        }

        private void BindOrbit()
        {
            _viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) _viewport.CapturePointer(evt.pointerId);
            });
            _viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_viewport.HasPointerCapture(evt.pointerId)) _stage.Orbit(evt.deltaPosition);
            });
            _viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_viewport.HasPointerCapture(evt.pointerId)) _viewport.ReleasePointer(evt.pointerId);
            });
            _viewport.RegisterCallback<WheelEvent>(evt =>
            {
                if (Mathf.Approximately(evt.delta.y, 0f)) return;
                _stage.Zoom(-Mathf.Sign(evt.delta.y));
                evt.StopPropagation();
            });
        }

        private void Resize()
        {
            var size = _viewport.contentRect.size;
            var canvas = _view.layout.size;
            if (!(size.x > 0f && size.y > 0f && canvas.x > 0f && canvas.y > 0f)) return;
            var pixels = new Vector2Int(Mathf.RoundToInt(size.x * Screen.width / canvas.x), Mathf.RoundToInt(size.y * Screen.height / canvas.y));
            var texture = _stage.Resize(pixels, _sampling);
            _viewport.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(texture));
            _resolution.text = $"{texture.width} × {texture.height}  ·  SMAA";
        }

        private static VisualElement Add(VisualElement host, string style)
        {
            var element = new VisualElement();
            element.AddToClassList(style);
            host.Add(element);
            return element;
        }

        private static Label Label(VisualElement host, string text, string style)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(style);
            host.Add(label);
            return label;
        }

        private static Button Button(VisualElement host, string text, string name, Action action = null)
        {
            var button = new Button(action) { text = text, name = name };
            button.AddToClassList("review-button");
            host.Add(button);
            return button;
        }

        private static VisualElement Group(VisualElement host, string title)
        {
            var group = Add(host, "review-group");
            Label(group, title, "review-group-title");
            return group;
        }

        private static void Choices(VisualElement parent, string name, string[] labels, Action<int> select)
        {
            var row = Add(parent, "review-row");
            var buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                buttons[i] = Button(row, labels[i], $"{name}-{i}", () =>
                {
                    select(index);
                    for (int j = 0; j < buttons.Length; j++) buttons[j].EnableInClassList("selected", j == index);
                });
            }
            buttons[0].AddToClassList("selected");
        }

        internal void Animate() => _stage.Animate();
        internal void Present() => _stage.Present();

        public void Dispose()
        {
            _view.RemoveFromHierarchy();
            _stage.Dispose();
        }
    }
}
