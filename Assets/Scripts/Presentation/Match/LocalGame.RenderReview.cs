using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    public sealed partial class LocalGame
    {
        private RenderReviewScreen _renderReview;
        private int _reviewPreviousMask;
        private CameraClearFlags _reviewPreviousClearFlags;
        private LayerMask _reviewPreviousVolumeMask;
        private bool _reviewPreviousPostProcessing;

        private void OpenRenderReview()
        {
            if (!InLobby || _preparing || _queuedStart || _changingMap || _renderReview != null) return;
            _renderReview = new RenderReviewScreen(_document.rootVisualElement, _catalog, _presentation, CloseRenderReview);
            _preview.Hide();
            _reviewPreviousMask = _camera.cullingMask;
            _reviewPreviousClearFlags = _camera.clearFlags;
            _camera.cullingMask = 0;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            if (_baseData)
            {
                _reviewPreviousVolumeMask = _baseData.volumeLayerMask;
                _reviewPreviousPostProcessing = _baseData.renderPostProcessing;
                _baseData.volumeLayerMask = LayerMask.GetMask("Render Review");
                _baseData.renderPostProcessing = false;
            }
            _lobbyHost.style.display = DisplayStyle.None;
        }

        private void CloseRenderReview()
        {
            if (_renderReview == null) return;
            ReleaseRenderReview();
            _lobbyHost.style.display = DisplayStyle.Flex;
            ShowParty(ReadWardrobe());
        }

        private void ReleaseRenderReview()
        {
            if (_renderReview == null) return;
            _renderReview.Dispose();
            _renderReview = null;
            if (_camera)
            {
                _camera.cullingMask = _reviewPreviousMask;
                _camera.clearFlags = _reviewPreviousClearFlags;
            }
            if (_baseData)
            {
                _baseData.volumeLayerMask = _reviewPreviousVolumeMask;
                _baseData.renderPostProcessing = _reviewPreviousPostProcessing;
            }
        }

        internal static float OrbitWheelSteps(float delta)
        {
            bool windows = Application.platform is RuntimePlatform.WindowsEditor or RuntimePlatform.WindowsPlayer;
            return windows && InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
                ? delta / 120f : delta;
        }
    }
}
