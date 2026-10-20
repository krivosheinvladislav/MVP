using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityMvp.Interfaces;

namespace UnityMvp.Support
{
    public sealed class ViewLoader : IViewLoader, IDisposable
    {
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _handles = new();
        private readonly Dictionary<string, ViewLoadOperation> _loadingOperations = new();

        private bool _isDisposed;

        public async UniTask<TView> LoadViewAsync<TView>(
            string address,
            Transform parent,
            CancellationToken cancellationToken)
            where TView : Component, IView
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ViewLoader));

            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("View address is empty.", nameof(address));

            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetLoadedInstance(address, out GameObject loadedInstance))
                return GetView<TView>(address, loadedInstance);

            if (!_loadingOperations.TryGetValue(address, out ViewLoadOperation operation))
            {
                operation = new ViewLoadOperation();
                _loadingOperations.Add(address, operation);
                LoadInstanceAsync(address, parent, operation).Forget();
            }

            GameObject instance = await operation.CompletionSource.Task.AttachExternalCancellation(cancellationToken);
            return GetView<TView>(address, instance);
        }

        public void UnloadView(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;

            if (_loadingOperations.Remove(address, out ViewLoadOperation operation))
                operation.RequestRelease();

            if (!_handles.Remove(address, out AsyncOperationHandle<GameObject> handle))
                return;

            ReleaseHandle(handle);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            AsyncOperationHandle<GameObject>[] handles = new AsyncOperationHandle<GameObject>[_handles.Count];
            _handles.Values.CopyTo(handles, 0);
            ViewLoadOperation[] operations = new ViewLoadOperation[_loadingOperations.Count];
            _loadingOperations.Values.CopyTo(operations, 0);
            _handles.Clear();
            _loadingOperations.Clear();

            foreach (ViewLoadOperation operation in operations)
                operation.RequestRelease();

            foreach (AsyncOperationHandle<GameObject> handle in handles)
                ReleaseHandle(handle);
        }

        private async UniTask LoadInstanceAsync(string address, Transform parent, ViewLoadOperation operation)
        {
            AsyncOperationHandle<GameObject> handle = default;
            bool hasHandle = false;

            try
            {
                handle = Addressables.InstantiateAsync(address, parent);
                hasHandle = true;

                GameObject instance = await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded || !instance)
                    throw new InvalidOperationException($"Failed to instantiate view {address}.", handle.OperationException);

                if (_isDisposed || operation.IsReleaseRequested)
                {
                    ReleaseHandle(handle);
                    operation.RequestRelease();
                    return;
                }

                _handles[address] = handle;
                operation.CompletionSource.TrySetResult(instance);
            }
            catch (Exception exception)
            {
                if (hasHandle)
                    ReleaseHandle(handle);

                if (_isDisposed || operation.IsReleaseRequested)
                {
                    operation.RequestRelease();
                    return;
                }

                operation.CompletionSource.TrySetException(exception);
            }
            finally
            {
                if (_loadingOperations.TryGetValue(address, out ViewLoadOperation currentOperation) && ReferenceEquals(currentOperation, operation))
                    _loadingOperations.Remove(address);
            }
        }

        private bool TryGetLoadedInstance(string address, out GameObject instance)
        {
            instance = null;

            if (!_handles.TryGetValue(address, out AsyncOperationHandle<GameObject> handle))
                return false;

            if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result)
            {
                instance = handle.Result;
                return true;
            }

            _handles.Remove(address);
            ReleaseHandle(handle);

            return false;
        }

        private TView GetView<TView>(string address, GameObject instance)
            where TView : Component, IView
        {
            if (!instance)
            {
                UnloadView(address);
                throw new InvalidOperationException($"View instance {address} was destroyed.");
            }

            if (instance.TryGetComponent(out TView view) && !view.IsBeingDestroyed)
                return view;

            UnloadView(address);
            throw new MissingComponentException($"Component {typeof(TView).Name} was not found on view {address}.");
        }

        private static void ReleaseHandle(AsyncOperationHandle<GameObject> handle)
        {
            try
            {
                if (handle.IsValid())
                    Addressables.ReleaseInstance(handle);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private sealed class ViewLoadOperation
        {
            public UniTaskCompletionSource<GameObject> CompletionSource { get; } = new();
            public bool IsReleaseRequested { get; private set; }

            public void RequestRelease()
            {
                if (IsReleaseRequested)
                    return;

                IsReleaseRequested = true;
                CompletionSource.TrySetCanceled();
            }
        }
    }
}
