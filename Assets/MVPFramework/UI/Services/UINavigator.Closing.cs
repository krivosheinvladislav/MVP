using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityMvp.Interfaces;

namespace UnityMvp
{
    public sealed partial class UINavigator
    {
        private void CancelPresenterOpening(PresenterEntry entry)
        {
            entry.CloseCompletionSource = new UniTaskCompletionSource();
            entry.State = NavigationState.Closing;
            CancelSafely(entry.OperationCts);
        }

        private void ClosePresenter(Type type, PresenterEntry entry)
        {
            entry.CloseCompletionSource = new UniTaskCompletionSource();
            entry.State = NavigationState.Closing;
            CancelSafely(entry.OperationCts);
            FinishClose(type, entry, notifyClosed: true);
        }

        private void FinishClose(Type type, PresenterEntry entry, bool notifyClosed = false)
        {
            entry.CloseCompletionSource ??= new UniTaskCompletionSource();
            entry.State = NavigationState.Closing;
            UniTaskCompletionSource closingSource = entry.CloseCompletionSource;
            CancellationTokenSource operationCts = entry.OperationCts;
            CancelSafely(operationCts);

            if (entry.IsPrepared)
            {
                entry.IsPrepared = false;
                InvokeBeforeHideSafely(entry.Presenter);
                InvokeAfterHideSafely(entry.Presenter);
            }

            UnloadPresenterViewSafely(entry.Presenter);
            ClearDisposablesSafely(entry.Disposables);

            if (entry.IsCounted)
            {
                entry.IsCounted = false;

                if (!IsDisposed)
                    DecrementOpenedCounters(entry);
            }

            _suppressedPresenters.Remove(type);
            entry.ShowCompletionSource = null;
            entry.OperationCts = null;
            operationCts?.Dispose();

            if (notifyClosed && !IsDisposed)
                PublishPresenterClosedSafely(entry.Presenter);

            entry.State = NavigationState.Closed;
            entry.CloseCompletionSource = null;
            closingSource.TrySetResult();
        }

        private static void InvokeBeforeHideSafely(IPresenter presenter)
        {
            try
            {
                presenter.BeforeHide();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void InvokeAfterHideSafely(IPresenter presenter)
        {
            try
            {
                presenter.AfterHide();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void UnloadPresenterViewSafely(IPresenter presenter)
        {
            if (presenter is not IBasePresenter basePresenter)
                return;

            try
            {
                basePresenter.UnloadView();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void ClearDisposablesSafely(CompositeDisposable disposables)
        {
            foreach (IDisposable disposable in disposables)
            {
                try
                {
                    disposables.Remove(disposable);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void PublishPresenterClosedSafely(IPresenter presenter)
        {
            try
            {
                _onPresenterClosed.OnNext(presenter);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
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

        private static void DisposeSafely(IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
