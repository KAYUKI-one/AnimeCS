using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AnimeCS.Tests
{
#if UNITY_EDITOR
    [SetUpFixture]
    public sealed class ReplayInputEnvironment
    {
        private InputSettings _original, _replay;

        [OneTimeSetUp]
        public void RouteQueuedDeviceEventsToTheGame()
        {
            // Standalone render checks and the Test Runner can both take focus from the Game view.
            _original = InputSystem.settings;
            _replay = Object.Instantiate(_original);
            _replay.hideFlags = HideFlags.HideAndDontSave;
            _replay.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _replay.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _replay;
        }

        [OneTimeTearDown]
        public void RestoreInputSettings()
        {
            InputSystem.settings = _original;
            Object.Destroy(_replay);
        }
    }
#endif

    // The runtime panel's focus gate is separate from the Input System settings used by the fixture.
    internal sealed class UiReplayInput : IDisposable
    {
        private readonly PropertyInfo _mode;
        private readonly object _previous;
        private readonly List<Mouse> _otherMice = new();

        internal UiReplayInput(Mouse replayMouse)
        {
            var utility = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility", true);
            _mode = utility.GetProperty("eventSystemUpdateMode", BindingFlags.Static | BindingFlags.NonPublic);
            _previous = _mode.GetValue(null);
            foreach (var device in InputSystem.devices)
                if (device is Mouse mouse && mouse != replayMouse && mouse.enabled) _otherMice.Add(mouse);

            try
            {
                _mode.SetValue(null, Enum.Parse(_mode.PropertyType, "Always"));
                // InputForUI uses one last pointer position for all mice, including native cursor warps.
                foreach (var mouse in _otherMice) InputSystem.DisableDevice(mouse);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static Vector2 ScreenPoint(IPanel panel, Vector2 position)
        {
            var fraction = Rect.PointToNormalized(panel.visualTree.worldBound, position);
            return new Vector2(fraction.x * Screen.width, (1f - fraction.y) * Screen.height);
        }

        public void Dispose()
        {
            foreach (var mouse in _otherMice)
                if (mouse.added && !mouse.enabled) InputSystem.EnableDevice(mouse);
            _mode.SetValue(null, _previous);
        }
    }
}
