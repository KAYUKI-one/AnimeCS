#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AnimeCS.Tests
{
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
}
#endif
