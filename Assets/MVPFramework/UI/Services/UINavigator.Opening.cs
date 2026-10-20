using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityMvp.Interfaces;

namespace UnityMvp
{
    public sealed partial class UINavigator
    {
        private async UniTask<IPresenter> ShowInternalAsync(
            Type type,
            Transform parent,
            Action<IPresenter> preparePresenter = null)
        {
            ThrowIfDisposed();

            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!_registry.TryGetValue(type, out PresenterEntry entry))
                throw new InvalidOperationException($"Presenter of type {type.Name} not registered");

            while (true)
            {
                ThrowIfDisposed();
                switch (entry.State)
                {
                    case NavigationState.Open:
                        if (!HasValidView(entry.Presenter as IBasePresenter))
                        {
                            ClosePresenter(type, entry);
                            continue;
                        }

                        if (preparePresenter != null)
                            throw new InvalidOperationException("Hide the presenter before supplying a new payload.");

                        return entry.Presenter;
                    case NavigationState.Opening:
                        if (preparePresenter != null)
                            throw new InvalidOperationException("A presenter with a payload is already opening.");

                        return await entry.ShowCompletionSource.Task;
                    case NavigationState.Closing:
                        await entry.CloseCompletionSource.Task;
                        continue;
                    case NavigationState.Closed:
                        UniTaskCompletionSource<IPresenter> completionSource = new();
                        entry.ShowCompletionSource = completionSource;
                        entry.OperationCts = CancellationTokenSource.CreateLinkedTokenSource(DisposeToken);
                        entry.State = NavigationState.Opening;
                        RunShowAsync(type, entry, parent, preparePresenter).Forget();
                        return await completionSource.Task;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private async UniTask RunShowAsync(
            Type type,
            PresenterEntry entry,
            Transform parent,
            Action<IPresenter> preparePresenter)
        {
            UniTaskCompletionSource<IPresenter> completionSource = entry.ShowCompletionSource;
            CancellationTokenSource operationCts = entry.OperationCts;
            CancellationToken cancellationToken = operationCts.Token;

            try
            {
                await ShowPresenterAsync(type, entry, parent, preparePresenter, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!ReferenceEquals(entry.ShowCompletionSource, completionSource) || entry.State != NavigationState.Open)
                    throw new OperationCanceledException(cancellationToken);

                ApplySuppression(type);
                cancellationToken.ThrowIfCancellationRequested();

                if (!ReferenceEquals(entry.ShowCompletionSource, completionSource))
                    throw new OperationCanceledException(cancellationToken);

                if (!HasValidView(entry.Presenter as IBasePresenter))
                    throw new InvalidOperationException("Presenter view was destroyed while opening.");

                entry.ShowCompletionSource = null;
                completionSource.TrySetResult(entry.Presenter);
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(entry.OperationCts, operationCts))
                    FinishClose(type, entry);

                completionSource.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(entry.OperationCts, operationCts))
                    FinishClose(type, entry);

                completionSource.TrySetException(exception);
            }
        }

        private async UniTask ShowPresenterAsync(
            Type type,
            PresenterEntry entry,
            Transform parent,
            Action<IPresenter> preparePresenter,
            CancellationToken cancellationToken)
        {
            IPresenter presenter = entry.Presenter;
            IBasePresenter basePresenter = presenter as IBasePresenter;

            preparePresenter?.Invoke(presenter);
            cancellationToken.ThrowIfCancellationRequested();

            if (basePresenter != null && !IsPresenterViewValid(basePresenter))
                await basePresenter.LoadViewAsync(_temporaryRoot, _viewLoader, cancellationToken);

            ValidateShow(entry, basePresenter, cancellationToken);
            entry.IsPrepared = true;
            await presenter.BeforeShow(entry.Disposables, cancellationToken);
            ValidateShow(entry, basePresenter, cancellationToken);

            if (basePresenter != null)
            {
                Transform container = basePresenter.Container;
                Transform targetParent = GetTargetParent(type, entry, parent);

                if (!container || !targetParent)
                    throw new InvalidOperationException($"No valid UI root or view is configured for {type.Name}.");

                container.SetParent(targetParent, false);
                container.SetAsLastSibling();
            }

            presenter.AfterShow();

            ValidateShow(entry, basePresenter, cancellationToken);

            entry.State = NavigationState.Open;
            entry.IsCounted = true;
            IncrementOpenedCounters(entry);
        }

        private static void ValidateShow(
            PresenterEntry entry,
            IBasePresenter presenter,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.State != NavigationState.Opening)
                throw new OperationCanceledException(cancellationToken);

            if (!HasValidView(presenter))
                throw new InvalidOperationException("Presenter view was destroyed while opening.");
        }

        private Transform GetTargetParent(Type type, PresenterEntry entry, Transform parent)
        {
            if (parent)
                return parent;

            if (_layerRoots.TryGetValue(entry.Layer, out Transform layerRoot) && layerRoot)
                return layerRoot;

            throw new InvalidOperationException($"UI layer {entry.Layer} is not configured for {type.Name}.");
        }

        private static bool HasValidView(IBasePresenter presenter)
        {
            return presenter == null || IsPresenterViewValid(presenter);
        }

        private static bool IsPresenterViewValid(IBasePresenter presenter)
        {
            return presenter is { IsViewLoaded: true, Container: not null };
        }
    }
}
