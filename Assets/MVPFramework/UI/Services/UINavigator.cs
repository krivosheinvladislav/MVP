using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityMvp.Enums;
using UnityMvp.Infrastructure.Camera;
using UnityMvp.Interfaces;
using UnityMvp.Support;

namespace UnityMvp
{
    public sealed partial class UINavigator : IUINavigatorInitializable, IUINavigatorRegistrator, IUINavigator, IDisposable
    {
        private readonly ICameraProvider _cameraProvider;
        private readonly IViewLoader _viewLoader;
        private readonly Dictionary<Type, PresenterEntry> _registry = new();
        private readonly List<Type> _order = new();
        private readonly List<Type> _suppressedPresenters = new();
        private readonly Subject<IPresenter> _onPresenterClosed = new();
        private readonly ReactiveProperty<int> _activeOverlayWindows = new();
        private readonly CancellationTokenSource _disposeCts = new();

        private int _activeFullScreenWindows;
        private int _originalCullingMask;
        private int _suppressionVersion;
        private bool _disableSceneCameraForFullScreenWindows;
        private bool _isSuppressed;
        private bool _isApplyingSuppression;
        private bool _isDisposed;
        private Dictionary<UILayer, Transform> _layerRoots = new();
        private Transform _temporaryRoot;
        private Camera _camera;

        public UINavigator(ICameraProvider cameraProvider, IViewLoader viewLoader)
        {
            _cameraProvider = cameraProvider;
            _viewLoader = viewLoader;
        }

        public Observable<IPresenter> OnPresenterClosed => _onPresenterClosed;
        public Observable<int> ActiveOverlayWindows => _activeOverlayWindows;
        private bool IsDisposed => _isDisposed;
        private CancellationToken DisposeToken => _disposeCts.Token;

        public void Initialize(
            Dictionary<UILayer, Transform> layerRoots,
            Transform temporaryRoot,
            bool disableSceneCameraForFullScreenWindows)
        {
            if (IsDisposed)
                return;

            _layerRoots = layerRoots ?? new Dictionary<UILayer, Transform>();
            _temporaryRoot = temporaryRoot;
            _disableSceneCameraForFullScreenWindows = disableSceneCameraForFullScreenWindows;

            if (!_disableSceneCameraForFullScreenWindows)
                return;

            Camera camera = _cameraProvider.Current;

            if (!camera)
                throw new InvalidOperationException("Scene camera is required for full-screen suppression.");

            _camera = camera;
            _originalCullingMask = camera.cullingMask;
        }

        public void RegisterPresenter<T>(T presenter, string viewAddress, UILayer layer, bool isFullScreen)
            where T : IPresenter
        {
            RegisterPresenterInternal(typeof(T), presenter, viewAddress, layer, isFullScreen);
        }

        public void RegisterPresenter(IPresenter presenter, string viewAddress, UILayer layer, bool isFullScreen)
        {
            if (presenter == null)
                throw new ArgumentNullException(nameof(presenter));
            RegisterPresenterInternal(presenter.GetType(), presenter, viewAddress, layer, isFullScreen);
        }

        public async UniTask<T> ShowAsync<T>(Transform parent = null)
            where T : IPresenter
        {
            IPresenter result = await ShowInternalAsync(typeof(T), parent);
            return result is T presenter ? presenter : default;
        }

        public async UniTask<T> ShowAsync<T, TData>(TData data, Transform parent = null)
            where T : IPresenter
        {
            Type type = typeof(T);
            IPresenter result = await ShowInternalAsync(type, parent, presenter =>
            {
                if (presenter is IPayloadReceiver<TData> receiver)
                    receiver.ReceivePayload(data);
                else
                    throw new InvalidOperationException($"Presenter {type.Name} does not implement IPayloadReceiver<{typeof(TData).Name}>.");
            });

            return result is T presenter ? presenter : default;
        }

        public async UniTask ShowAsync(Type type, Transform parent = null)
        {
            await ShowInternalAsync(type, parent);
        }

        public void Hide<T>()
            where T : IPresenter
        {
            Hide(typeof(T));
        }

        public void Hide(Type type)
        {
            if (IsDisposed || type == null)
                return;

            if (!_registry.TryGetValue(type, out PresenterEntry entry))
                throw new InvalidOperationException($"Presenter of type {type.Name} not registered");

            switch (entry.State)
            {
                case NavigationState.Closed:
                    UnloadPresenterViewSafely(entry.Presenter);
                    return;
                case NavigationState.Closing:
                    return;
                case NavigationState.Opening:
                    CancelPresenterOpening(entry);
                    return;
                case NavigationState.Open:
                    ClosePresenter(type, entry);
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public T Resolve<T>()
            where T : IPresenter
        {
            ThrowIfDisposed();

            Type type = typeof(T);

            if (!_registry.TryGetValue(type, out PresenterEntry entry))
                throw new InvalidOperationException($"Presenter of type {type.Name} not registered");

            return (T)entry.Presenter;
        }

        public bool IsOpened<T>()
            where T : IPresenter
        {
            return !IsDisposed && _registry.TryGetValue(typeof(T), out PresenterEntry entry) && entry.State == NavigationState.Open && HasValidView(entry.Presenter as IBasePresenter);
        }

        public void SuppressAll()
        {
            if (_isSuppressed || IsDisposed)
                return;

            _isSuppressed = true;
            _suppressionVersion++;
            ApplySuppressionState();
        }

        public void RestoreAll()
        {
            if (!_isSuppressed || IsDisposed)
                return;

            _isSuppressed = false;
            _suppressionVersion++;
            ApplySuppressionState();
        }

        public async UniTask PreloadAsync<T>()
            where T : IPresenter
        {
            if (IsDisposed)
                return;

            Type type = typeof(T);

            if (!_registry.TryGetValue(type, out PresenterEntry entry))
                throw new InvalidOperationException($"Presenter of type {type.Name} not registered");

            while (entry.State == NavigationState.Closing || entry.State == NavigationState.Open && !HasValidView(entry.Presenter as IBasePresenter))
            {
                if (entry.State == NavigationState.Open)
                    ClosePresenter(type, entry);
                else
                    await entry.CloseCompletionSource.Task.AttachExternalCancellation(DisposeToken);

                if (IsDisposed)
                    return;
            }

            if (entry.Presenter is not IBasePresenter basePresenter || IsPresenterViewValid(basePresenter))
                return;

            await basePresenter.LoadViewAsync(_temporaryRoot, _viewLoader, DisposeToken);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            CancelSafely(_disposeCts);
            RestoreSceneCameraCullingMask();

            foreach (Type type in _order)
            {
                PresenterEntry entry = _registry[type];

                if (entry.State == NavigationState.Open)
                    ClosePresenter(type, entry);
                else if (entry.State == NavigationState.Closed)
                    UnloadPresenterViewSafely(entry.Presenter);
                else
                    CancelSafely(entry.OperationCts);
            }

            _activeFullScreenWindows = 0;
            SetOverlayWindowCount(0);
            _suppressedPresenters.Clear();
            _temporaryRoot = null;
            _camera = null;

            DisposeSafely(_onPresenterClosed);
            DisposeSafely(_activeOverlayWindows);
            DisposeSafely(_disposeCts);
        }

        private void RegisterPresenterInternal(
            Type type,
            IPresenter presenter,
            string viewAddress,
            UILayer layer,
            bool isFullScreen)
        {
            ThrowIfDisposed();

            if (presenter == null)
                throw new ArgumentNullException(nameof(presenter));

            if (string.IsNullOrWhiteSpace(viewAddress))
                throw new ArgumentException("View address is empty.", nameof(viewAddress));

            if (_registry.ContainsKey(type))
                throw new InvalidOperationException($"Presenter {type.Name} is already registered.");

            foreach (PresenterEntry registered in _registry.Values)
            {
                if (registered.ViewAddress == viewAddress || ReferenceEquals(registered.Presenter, presenter))
                    throw new InvalidOperationException($"View address {viewAddress} or presenter is already registered.");
            }

            if (presenter is IBasePresenter basePresenter)
                basePresenter.SetViewAddress(viewAddress);

            _registry.Add(type, new PresenterEntry(presenter, viewAddress, layer, isFullScreen));
            _order.Add(type);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(UINavigator));
        }

        private enum NavigationState : byte
        {
            Closed = 0,
            Opening = 1,
            Open = 2,
            Closing = 3
        }

        private sealed class PresenterEntry
        {
            public PresenterEntry(IPresenter presenter, string viewAddress, UILayer layer, bool isFullScreen)
            {
                Presenter = presenter;
                ViewAddress = viewAddress;
                Layer = layer;
                IsFullScreen = isFullScreen;
            }

            public IPresenter Presenter { get; }
            public CompositeDisposable Disposables { get; } = new();
            public UILayer Layer { get; }
            public bool IsFullScreen { get; }
            public NavigationState State { get; set; }
            public UniTaskCompletionSource<IPresenter> ShowCompletionSource { get; set; }
            public UniTaskCompletionSource CloseCompletionSource { get; set; }
            public CancellationTokenSource OperationCts { get; set; }
            public bool IsPrepared { get; set; }
            public bool IsCounted { get; set; }
            public string ViewAddress { get; }
        }
    }
}
