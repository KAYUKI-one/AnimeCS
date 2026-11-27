using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class EquipmentDrag<TItem>
    {
        private const long HoldMilliseconds = 180;
        private const float SnapDistance = 5f;
        private const float CardHeight = 104f;
        private readonly VisualElement _root;
        private readonly Dictionary<Button, (TItem item, int slot, Action activate)> _sources = new();
        private readonly Dictionary<VisualElement, int> _slots = new();
        private readonly Action<VisualElement, TItem> _renderCard;
        private readonly IVisualElementScheduledItem _hold;
        private Button _source;
        private VisualElement _eligible, _ghost, _outline;
        private float _ghostWidth;
        private int _pointer = -1;
        private Vector2 _position;

        internal event Action<TItem, int> ItemRequested;

        internal EquipmentDrag(VisualElement root, Action<VisualElement, TItem> renderCard)
        {
            _root = root;
            _renderCard = renderCard;
            _hold = root.schedule.Execute(BeginDrag);
            _hold.Pause();
            root.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(evt => { if (evt.pointerId == _pointer) CancelDrag(); });
            root.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (evt.target == _root && evt.pointerId == _pointer && !_root.HasPointerCapture(_pointer)) CancelDrag();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ => CancelDrag());
        }

        internal void Source(Button button, TItem item, int slot)
        {
            void Activate() => ItemRequested?.Invoke(item, slot);
            button.clicked += Activate;
            _sources.Add(button, (item, slot, Activate));
        }

        internal void Slot(VisualElement element, int slot) => _slots.Add(element, slot);

        internal void Clear()
        {
            CancelDrag();
            foreach (var source in _sources) source.Key.clicked -= source.Value.activate;
            _sources.Clear();
            _slots.Clear();
        }

        private void Down(PointerDownEvent evt)
        {
            if (_pointer >= 0 || evt.button != 0 || evt.pointerId != PointerId.mousePointerId) return;
            for (var element = evt.target as VisualElement; element != null; element = element.parent)
            {
                if (element is not Button button || !button.enabledInHierarchy || !_sources.ContainsKey(button)) continue;
                _source = button;
                _pointer = evt.pointerId;
                _position = evt.position;
                _hold.ExecuteLater(HoldMilliseconds);
                button.Focus();

                // A Button would otherwise capture on press and receive subsequent pointer events exclusively.
                _root.CapturePointer(_pointer);
                evt.StopImmediatePropagation();
                return;
            }
        }

        private void Move(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointer) return;
            evt.StopImmediatePropagation();
            _position = evt.position;
            if ((evt.pressedButtons & 1) == 0)
            {
                // The input backend can report releasing the left button as a move when another button is held.
                if (evt.button == 0) Finish();
                else CancelDrag();
                return;
            }
            if (_ghost != null) TrackPointer();
        }

        private void BeginDrag()
        {
            if (_source == null || _source.panel != _root.panel || !_root.HasPointerCapture(_pointer))
            {
                CancelDrag();
                return;
            }

            var (item, destination, _) = _sources[_source];
            foreach (var target in _slots)
            {
                if (target.Value != destination) continue;
                _eligible = target.Key;
                _eligible.AddToClassList("drop-ready");
                _outline = new VisualElement { pickingMode = PickingMode.Ignore };
                _outline.AddToClassList("drop-outline");
                _outline.generateVisualContent += DrawOutline;
                _eligible.Add(_outline);
                break;
            }
            _source.AddToClassList("drag-source");
            _ghost = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            _ghostWidth = Mathf.Clamp(_source.layout.width * 1.15f, 118f, 174f);
            _ghost.style.position = Position.Absolute;
            _ghost.style.left = 0;
            _ghost.style.top = 0;
            _ghost.style.width = _ghostWidth;
            _ghost.style.height = CardHeight;
            _ghost.AddToClassList("equipment-drag-ghost");
            _renderCard(_ghost, item);
            IgnorePicking(_ghost);
            _root.Add(_ghost);
            TrackPointer();
        }

        private static void IgnorePicking(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < element.hierarchy.childCount; i++) IgnorePicking(element.hierarchy[i]);
        }

        private void TrackPointer()
        {
            bool snapped = CanSnap(_position);
            var local = _root.WorldToLocal(_position);
            float x = Mathf.Clamp(local.x + 14, 0, Mathf.Max(0, _root.layout.width - _ghostWidth));
            float y = Mathf.Clamp(local.y + 12, 0, Mathf.Max(0, _root.layout.height - CardHeight));
            _ghost.style.translate = new Translate(x, y);
            _eligible?.EnableInClassList("drop-snapped", snapped);
            _ghost.EnableInClassList("snapped", snapped);
        }

        private bool CanSnap(Vector2 position)
        {
            if (_eligible == null || !_eligible.enabledInHierarchy || !_root.worldBound.Contains(position)) return false;
            for (var element = _root.panel.Pick(position); element != null; element = element.parent)
                if (_slots.ContainsKey(element)) return element == _eligible;

            var bounds = _eligible.worldBound;
            if (bounds.Contains(position)) return false;
            var scroll = _eligible.GetFirstAncestorOfType<ScrollView>();
            var viewport = scroll?.contentViewport.worldBound ?? _root.worldBound;
            if (!bounds.Overlaps(viewport)) return false;
            // Use only the visible part of a slot when snapping near the edge of a scroll view.
            var nearest = new Vector2(
                Mathf.Clamp(position.x, Mathf.Max(bounds.xMin, viewport.xMin), Mathf.Min(bounds.xMax, viewport.xMax)),
                Mathf.Clamp(position.y, Mathf.Max(bounds.yMin, viewport.yMin), Mathf.Min(bounds.yMax, viewport.yMax)));
            return (position - nearest).sqrMagnitude <= SnapDistance * SnapDistance;
        }

        private static void DrawOutline(MeshGenerationContext context)
        {
            var rect = context.visualElement.contentRect;
            const float inset = 2f;
            if (rect.width < 2 * inset || rect.height < 2 * inset) return;
            var painter = context.painter2D;
            painter.lineWidth = 1.25f;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = new Color32(43, 147, 175, 255);
            painter.BeginPath();
            Dashes(painter, new Vector2(inset, inset), Vector2.right, rect.width - 2 * inset);
            Dashes(painter, new Vector2(inset, rect.height - inset), Vector2.right, rect.width - 2 * inset);
            Dashes(painter, new Vector2(inset, inset), Vector2.up, rect.height - 2 * inset);
            Dashes(painter, new Vector2(rect.width - inset, inset), Vector2.up, rect.height - 2 * inset);
            painter.Stroke();
        }

        private static void Dashes(Painter2D painter, Vector2 start, Vector2 direction, float length)
        {
            for (float offset = 3f; offset < length - 3f; offset += 6f)
            {
                painter.MoveTo(start + direction * offset);
                painter.LineTo(start + direction * Mathf.Min(offset + 3f, length - 3f));
            }
        }

        private void Up(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointer) return;
            evt.StopImmediatePropagation();
            _position = evt.position;
            Finish();
        }

        private void Finish()
        {
            var (item, slot, _) = _sources[_source];
            bool request;
            if (_ghost != null) request = CanSnap(_position);
            else
            {
                var hit = _root.panel.Pick(_position);
                request = _source.enabledInHierarchy && (hit == _source || _source.Contains(hit));
            }

            CancelDrag();
            if (request) ItemRequested?.Invoke(item, slot);
        }

        internal bool CancelDrag()
        {
            if (_pointer < 0) return false;
            int pointer = _pointer;
            _pointer = -1;
            _hold.Pause();
            _source?.RemoveFromClassList("drag-source");
            _source = null;
            if (_ghost != null)
            {
                _ghost.RemoveFromHierarchy();
                _ghost = null;
                _outline?.RemoveFromHierarchy();
                _outline = null;
                _eligible?.RemoveFromClassList("drop-ready");
                _eligible?.RemoveFromClassList("drop-snapped");
                _eligible = null;
            }
            if (_root.panel != null && _root.HasPointerCapture(pointer)) _root.ReleasePointer(pointer);
            return true;
        }
    }
}
