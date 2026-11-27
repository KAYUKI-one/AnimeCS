using System;
using System.Collections.Generic;
using AnimeCS.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed class EquipmentDrag
    {
        private const float DragThreshold = 7f;
        private const float SnapDistance = 16f;
        private const float CardHeight = 104f;
        private readonly VisualElement _root;
        private readonly Dictionary<VisualElement, ContentDefinition> _sources = new();
        private readonly Dictionary<VisualElement, int> _slots = new();
        private readonly Func<ContentDefinition, int> _destination;
        private readonly Action<ContentDefinition, int> _drop;
        private readonly Action<VisualElement, ContentDefinition> _renderCard;
        private ContentDefinition _item;
        private VisualElement _source, _eligible, _ghost, _outline;
        private bool _snapped;
        private float _ghostWidth;
        private int _pointer = -1;
        private Vector2 _origin;

        internal EquipmentDrag(VisualElement root, Func<ContentDefinition, int> destination,
            Action<ContentDefinition, int> drop, Action<VisualElement, ContentDefinition> renderCard)
        {
            _root = root;
            _destination = destination;
            _drop = drop;
            _renderCard = renderCard;
            root.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(_ => Cancel());
            root.RegisterCallback<PointerCaptureOutEvent>(evt => { if (evt.target == _root) Cancel(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => Clear());
        }

        internal void Source(VisualElement element, ContentDefinition definition) => _sources.Add(element, definition);
        internal void Slot(VisualElement element, int slot) => _slots.Add(element, slot);

        internal bool CancelDrag()
        {
            if (_ghost == null) return false;
            Cancel();
            return true;
        }

        internal void Clear()
        {
            Cancel();
            _sources.Clear();
            _slots.Clear();
        }

        private void Down(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            Cancel();
            for (var element = evt.target as VisualElement; element != null; element = element.parent)
            {
                if (!_sources.TryGetValue(element, out var item)) continue;
                _item = item;
                _source = element;
                _pointer = evt.pointerId;
                _origin = evt.position;
                break;
            }
        }

        private void Move(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointer) return;
            Vector2 point = evt.position;
            if (_ghost == null)
            {
                if ((point - _origin).sqrMagnitude < DragThreshold * DragThreshold) return;
                BeginDrag();
            }
            _snapped = CanSnap(point);
            _eligible?.EnableInClassList("drop-snapped", _snapped);
            var local = _root.WorldToLocal(point);
            var anchor = _snapped ? _root.WorldToLocal(_eligible.worldBound.center) : local;
            _ghost.style.left = Mathf.Clamp(anchor.x + 14, 0, Mathf.Max(0, _root.layout.width - _ghostWidth));
            _ghost.style.top = Mathf.Clamp(anchor.y + 12, 0, Mathf.Max(0, _root.layout.height - CardHeight));
            _ghost.EnableInClassList("snapped", _snapped);
            evt.StopImmediatePropagation();
        }

        private void BeginDrag()
        {
            int destination = _destination(_item);
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
            _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            _ghostWidth = Mathf.Clamp(_source.layout.width * 1.15f, 118f, 174f);
            _ghost.style.width = _ghostWidth;
            _ghost.style.height = CardHeight;
            _ghost.AddToClassList("equipment-drag-ghost");
            _renderCard(_ghost, _item);
            _root.Add(_ghost);
            _root.CapturePointer(_pointer);
        }

        private bool CanSnap(Vector2 position)
        {
            if (_eligible == null || !_root.worldBound.Contains(position)) return false;
            for (var element = _root.panel?.Pick(position); element != null; element = element.parent)
                if (_slots.ContainsKey(element)) return element == _eligible;

            var bounds = _eligible.worldBound;
            var viewport = _eligible.GetFirstAncestorOfType<ScrollView>().contentViewport.worldBound;
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
            int destination = _ghost != null && CanSnap(evt.position) ? _slots[_eligible] : -1;
            var item = _item;
            if (_ghost != null) evt.StopImmediatePropagation();
            Cancel();
            if (destination >= 0) _drop(item, destination);
        }

        private void Cancel()
        {
            int pointer = _pointer;
            _pointer = -1;
            _item = null;
            _snapped = false;
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
            if (pointer >= 0 && _root.panel != null && _root.HasPointerCapture(pointer)) _root.ReleasePointer(pointer);
        }
    }
}
