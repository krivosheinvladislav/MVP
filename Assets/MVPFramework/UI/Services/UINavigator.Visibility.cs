using System;
using UnityEngine;
using UnityMvp.Enums;
using UnityMvp.Interfaces;

namespace UnityMvp
{
    public sealed partial class UINavigator
    {
        private void ApplySuppressionState()
        {
            if (_isApplyingSuppression || IsDisposed)
                return;

            _isApplyingSuppression = true;

            try
            {
                while (!IsDisposed)
                {
                    int suppressionVersion = _suppressionVersion;
                    bool suppress = _isSuppressed;
                    Type[] presenters = suppress ? _order.ToArray() : _suppressedPresenters.ToArray();

                    foreach (Type type in presenters)
                    {
                        if (IsDisposed || suppressionVersion != _suppressionVersion)
                            break;

                        if (!_registry.TryGetValue(type, out PresenterEntry entry))
                            continue;

                        if (suppress)
                            SuppressPresenter(type, entry);
                        else
                            RestorePresenter(type, entry);
                    }

                    if (suppressionVersion == _suppressionVersion)
                        break;
                }
            }
            finally
            {
                _isApplyingSuppression = false;
            }
        }

        private void RestorePresenter(Type type, PresenterEntry entry)
        {
            _suppressedPresenters.Remove(type);

            if (entry.State != NavigationState.Open || entry.Presenter is not IBasePresenter basePresenter)
                return;

            Transform container = basePresenter.Container;

            if (container)
                container.gameObject.SetActive(true);
        }

        private void SuppressPresenter(Type type, PresenterEntry entry)
        {
            if (entry.State != NavigationState.Open)
                return;

            if (entry.Presenter is ISuppressIgnorable)
                return;

            if (entry.Presenter is not IBasePresenter basePresenter)
                return;

            Transform container = basePresenter.Container;

            if (!container || !container.gameObject.activeSelf)
                return;

            if (!_suppressedPresenters.Contains(type))
                _suppressedPresenters.Add(type);

            container.gameObject.SetActive(false);
        }

        private void ApplySuppression(Type type)
        {
            if (!_isSuppressed)
                return;

            if (_suppressedPresenters.Contains(type))
                return;

            _suppressionVersion++;
            ApplySuppressionState();
        }

        private void IncrementOpenedCounters(PresenterEntry entry)
        {
            if (entry.IsFullScreen)
            {
                _activeFullScreenWindows++;
                UpdateFullScreenState();
                return;
            }

            if (IsWindowLayer(entry.Layer))
                SetOverlayWindowCount(_activeOverlayWindows.Value + 1);
        }

        private void DecrementOpenedCounters(PresenterEntry entry)
        {
            if (entry.IsFullScreen)
            {
                if (_activeFullScreenWindows > 0)
                    _activeFullScreenWindows--;

                UpdateFullScreenState();
                return;
            }

            if (IsWindowLayer(entry.Layer) && _activeOverlayWindows.Value > 0)
                SetOverlayWindowCount(_activeOverlayWindows.Value - 1);
        }

        private void SetOverlayWindowCount(int count)
        {
            if (_activeOverlayWindows.Value != count)
                _activeOverlayWindows.Value = count;
        }

        private void UpdateFullScreenState()
        {
            if (!_disableSceneCameraForFullScreenWindows)
                return;

            if (_camera)
                _camera.cullingMask = _activeFullScreenWindows > 0 ? 0 : _originalCullingMask;
        }

        private void RestoreSceneCameraCullingMask()
        {
            if (_camera)
                _camera.cullingMask = _originalCullingMask;
        }

        private static bool IsWindowLayer(UILayer layer)
        {
            return layer == UILayer.Window;
        }
    }
}
