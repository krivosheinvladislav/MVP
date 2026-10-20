using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityMvp.Interfaces;
using UnityMvp.Support;

namespace UnityMvp
{
    public abstract class BasePresenter<TView> : IPresenter, IBasePresenter where TView : Component, IView
    {
        private string _viewAddress;
        private int _loadRequestId;
        private CancellationTokenSource _loadCts;
        private UniTaskCompletionSource<TView> _loadCompletionSource;
        private IViewLoader _viewLoader;

        public TView View { get; private set; }
        public bool IsViewLoaded { get; private set; }
        public bool IsViewRequested => _loadCompletionSource != null;
        public Transform Container => IsViewLoaded && View != null && !View.IsBeingDestroyed ? View.transform : null;

        public void SetViewAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("View address is empty.", nameof(address));

            if (_viewAddress != null && _viewAddress != address)
                throw new InvalidOperationException("View address cannot change after its first assignment.");

            _viewAddress = address;
        }

        public async UniTask LoadViewAsync(
            Transform parent,
            IViewLoader viewLoader,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(_viewAddress))
                throw new InvalidOperationException($"View address is empty for {typeof(TView).Name}.");

            if (IsViewLoaded && Container)
                return;

            if (IsViewLoaded)
                UnloadView();

            UniTaskCompletionSource<TView> activeCompletionSource = _loadCompletionSource;

            if (activeCompletionSource != null)
            {
                await activeCompletionSource.Task.AttachExternalCancellation(cancellationToken);
                return;
            }

            IViewLoader activeViewLoader = viewLoader ?? throw new ArgumentNullException(nameof(viewLoader));
            int loadRequestId = ++_loadRequestId;
            CancellationTokenSource loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            UniTaskCompletionSource<TView> completionSource = new();

            _viewLoader = activeViewLoader;
            _loadCts = loadCts;
            _loadCompletionSource = completionSource;

            try
            {
                TView loadedView = await activeViewLoader.LoadViewAsync<TView>(_viewAddress, parent, loadCts.Token);

                if (loadRequestId != _loadRequestId || loadCts.IsCancellationRequested)
                    throw new OperationCanceledException(loadCts.Token);

                if (!loadedView || loadedView.IsBeingDestroyed)
                    throw new InvalidOperationException($"View {_viewAddress} was not loaded.");

                View = loadedView;
                IsViewLoaded = true;

                AfterLoad();

                if (loadRequestId != _loadRequestId || loadCts.IsCancellationRequested)
                    throw new OperationCanceledException(loadCts.Token);

                completionSource.TrySetResult(loadedView);
            }
            catch (OperationCanceledException)
            {
                if (loadRequestId == _loadRequestId)
                    ReleaseFailedLoad(activeViewLoader);

                completionSource.TrySetCanceled(loadCts.Token);
                throw;
            }
            catch (Exception exception)
            {
                if (loadRequestId == _loadRequestId)
                    ReleaseFailedLoad(activeViewLoader);

                completionSource.TrySetException(exception);
                await completionSource.Task;
                throw;
            }
            finally
            {
                if (ReferenceEquals(_loadCompletionSource, completionSource))
                    _loadCompletionSource = null;

                if (ReferenceEquals(_loadCts, loadCts))
                    _loadCts = null;

                loadCts.Dispose();
            }
        }

        public void UnloadView()
        {
            _loadRequestId++;

            CancellationTokenSource loadCts = _loadCts;
            UniTaskCompletionSource<TView> completionSource = _loadCompletionSource;
            IViewLoader activeViewLoader = _viewLoader;

            CancelSafely(loadCts);
            completionSource?.TrySetCanceled();

            try
            {
                activeViewLoader?.UnloadView(_viewAddress);
            }
            finally
            {
                ResetViewState();
            }
        }

        public virtual void AfterLoad()
        {
        }

        public abstract UniTask BeforeShow(CompositeDisposable disposables, CancellationToken cancellationToken);

        public virtual void AfterShow()
        {
            if (!View || View.IsBeingDestroyed)
            {
                Debug.LogError($"{nameof(AfterShow)} View is null");
                return;
            }

            View.gameObject.SetActive(true);
        }

        public virtual void BeforeHide()
        {
        }

        public virtual void AfterHide()
        {
            if (View && !View.IsBeingDestroyed)
                View.gameObject.SetActive(false);
        }

        private void ReleaseFailedLoad(IViewLoader activeViewLoader)
        {
            try
            {
                activeViewLoader.UnloadView(_viewAddress);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                ResetViewState();
            }
        }

        private void ResetViewState()
        {
            View = null;
            IsViewLoaded = false;
            _loadCompletionSource = null;
            _loadCts = null;
            _viewLoader = null;
        }

        private static void CancelSafely(CancellationTokenSource cancellationTokenSource)
        {
            if (cancellationTokenSource == null)
                return;

            try
            {
                cancellationTokenSource.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    public abstract class BasePresenter<TView, TModel> : BasePresenter<TView>, IModelSetter<TModel> where TView : Component, IView
    {
        public TModel Model { get; private set; }

        public void SetModel(TModel model)
        {
            Model = model;
        }
    }
}
