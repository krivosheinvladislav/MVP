using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityMvp;
using UnityMvp.Enums;
using Object = UnityEngine.Object;

namespace UnityMvp.Tests.EditMode
{
    public sealed class UINavigatorLifecycleTests
    {
        private MvpTestContext _context;

        [SetUp]
        public void SetUp()
        {
            _context = new MvpTestContext();
        }

        [TearDown]
        public void TearDown()
        {
            _context.Dispose();
        }

        [Test]
        public async Task ConcurrentShowsUseSingleLoad()
        {
            Task<TestPresenter> first = Show();
            Task<TestPresenter> second = Show();
            Assert.That(_context.Loader.LoadCount, Is.EqualTo(1));
            _context.Loader.Complete();
            Assert.That(await first, Is.SameAs(await second));
            Assert.That(_context.Presenter.AfterShowCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CanceledWaiterDoesNotCancelSharedShow()
        {
            using CancellationTokenSource cancellation = new();
            Task<TestPresenter> first = _context.Navigator.ShowAsync<TestPresenter>().AttachExternalCancellation(cancellation.Token).AsTask();
            Task<TestPresenter> second = Show();
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            _context.Loader.Complete();
            Assert.That(await second, Is.SameAs(_context.Presenter));
        }

        [Test]
        public async Task HideDuringLoadCancelsOpeningAndAllowsRetry()
        {
            Task<TestPresenter> first = Show();
            _context.Navigator.Hide<TestPresenter>();
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Task<TestPresenter> second = Show();
            Assert.That(_context.Loader.LoadCount, Is.EqualTo(2));
            _context.Loader.Complete();
            await second;
            Assert.That(_context.Navigator.IsOpened<TestPresenter>(), Is.True);
        }

        [Test]
        public void DisposeDuringLoadCancelsOpening()
        {
            Task<TestPresenter> show = Show();
            _context.Navigator.Dispose();
            Assert.CatchAsync<OperationCanceledException>(async () => await show);
            Assert.That(_context.Presenter.AfterShowCount, Is.Zero);
        }

        [Test]
        public void HideDuringPreparationCleansSubscriptionsAndHooks()
        {
            _context.Presenter.Preparation = new UniTaskCompletionSource();
            Task<TestPresenter> show = Show();
            _context.Loader.Complete();
            _context.Navigator.Hide<TestPresenter>();
            Assert.CatchAsync<OperationCanceledException>(async () => await show);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(_context.Presenter.BeforeHideCount, Is.EqualTo(1));
            Assert.That(_context.Presenter.View, Is.Null);
        }

        [Test]
        public void DisposeDuringPreparationCleansSubscriptions()
        {
            _context.Presenter.Preparation = new UniTaskCompletionSource();
            Task<TestPresenter> show = Show();
            _context.Loader.Complete();
            _context.Navigator.Dispose();
            Assert.CatchAsync<OperationCanceledException>(async () => await show);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(_context.Presenter.View, Is.Null);
        }

        [Test]
        public void FailedPreparationReachesAllWaitersAndCleansHooks()
        {
            _context.Presenter.ThrowOnBeforeShow = true;
            Task<TestPresenter> first = Show();
            Task<TestPresenter> second = Show();
            _context.Loader.Complete();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(_context.Presenter.BeforeHideCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyedViewDuringPreparationIsNotActivated()
        {
            _context.Presenter.Preparation = new UniTaskCompletionSource();
            Task<TestPresenter> show = Show();
            _context.Loader.Complete();
            Object.DestroyImmediate(_context.Presenter.View.gameObject);
            _context.Presenter.Preparation.TrySetResult();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await show);
            Assert.That(_context.Presenter.AfterShowCount, Is.Zero);
        }

        [Test]
        public async Task FailedLoadReachesAllWaitersAndCanBeRetried()
        {
            Task<TestPresenter> first = Show();
            Task<TestPresenter> second = Show();
            _context.Loader.Complete(false);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
            Task<TestPresenter> retry = Show();
            _context.Loader.Complete();
            await retry;
        }

        [Test]
        public async Task DestroyedOpenViewCanBeLoadedAgain()
        {
            Task<TestPresenter> first = Show();
            _context.Loader.Complete();
            await first;
            Object.DestroyImmediate(_context.Presenter.View.gameObject);
            Assert.That(_context.Navigator.IsOpened<TestPresenter>(), Is.False);
            Task<TestPresenter> second = Show();
            _context.Loader.Complete();
            await second;
            Assert.That(_context.Loader.LoadCount, Is.EqualTo(2));
        }

        [Test]
        public async Task ShowFromBeforeHideWaitsForCleanup()
        {
            Task<TestPresenter> first = Show();
            _context.Loader.Complete();
            await first;
            Task<TestPresenter> reopen = null;
            _context.Presenter.BeforeHideAction = () => reopen = Show();
            _context.Navigator.Hide<TestPresenter>();
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(_context.Loader.LoadCount, Is.EqualTo(2));
            _context.Presenter.BeforeHideAction = null;
            _context.Loader.Complete();
            await reopen;
            Assert.That(_context.Presenter.LiveSubscriptions, Is.EqualTo(1));
        }

        [Test]
        public async Task HideFromOverlayObserverDoesNotReviveOldAttempt()
        {
            using IDisposable subscription = _context.Navigator.ActiveOverlayWindows.Subscribe(count =>
            {
                if (count == 1)
                    _context.Navigator.Hide<TestPresenter>();
            });
            Task<TestPresenter> show = Show();
            _context.Loader.Complete();
            Assert.CatchAsync<OperationCanceledException>(async () => await show);
            Assert.That(_context.Navigator.IsOpened<TestPresenter>(), Is.False);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            await Task.CompletedTask;
        }

        [Test]
        public void HideFromAfterShowCancelsActivation()
        {
            _context.Presenter.AfterShowAction = () => _context.Navigator.Hide<TestPresenter>();
            Task<TestPresenter> show = Show();
            _context.Loader.Complete();
            Assert.CatchAsync<OperationCanceledException>(async () => await show);
            Assert.That(_context.Presenter.View, Is.Null);
        }

        [Test]
        public async Task PreloadAndShowShareOneLoad()
        {
            Task preload = _context.Navigator.PreloadAsync<TestPresenter>().AsTask();
            Task<TestPresenter> show = Show();
            _context.Loader.Complete();
            await preload;
            await show;
            Assert.That(_context.Loader.LoadCount, Is.EqualTo(1));
        }

        [Test]
        public async Task HideReleasesClosedPreloadedView()
        {
            Task preload = _context.Navigator.PreloadAsync<TestPresenter>().AsTask();
            _context.Loader.Complete();
            await preload;
            Assert.That(_context.Presenter.Container.gameObject.activeInHierarchy, Is.False);
            _context.Navigator.Hide<TestPresenter>();
            Assert.That(_context.Presenter.View, Is.Null);
        }

        [Test]
        public async Task FiftyCyclesDoNotAccumulateSubscriptionsOrViews()
        {
            for (int cycle = 0; cycle < 50; cycle++)
            {
                Task<TestPresenter> show = Show();
                _context.Loader.Complete();
                await show;
                Assert.That(_context.Presenter.LiveSubscriptions, Is.EqualTo(1));
                _context.Navigator.Hide<TestPresenter>();
                Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
                Assert.That(_context.Presenter.View, Is.Null);
            }

            Assert.That(_context.Loader.UnloadCount, Is.EqualTo(50));
        }

        [Test]
        public async Task PreloadAfterDestroyedOpenViewCleansOldOpeningBeforeReload()
        {
            Task<TestPresenter> first = Show();
            _context.Loader.Complete();
            await first;
            CancellationToken oldToken = _context.Presenter.ShowToken;
            Object.DestroyImmediate(_context.Presenter.View.gameObject);

            Task preload = _context.Navigator.PreloadAsync<TestPresenter>().AsTask();
            Assert.That(oldToken.IsCancellationRequested, Is.True);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(_context.Presenter.BeforeHideCount, Is.EqualTo(1));
            _context.Loader.Complete();
            await preload;
            Assert.That(_context.Navigator.IsOpened<TestPresenter>(), Is.False);
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.False);

            await Show();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(_context.Presenter.AfterShowCount, Is.EqualTo(2));
            Assert.That(_context.Presenter.LiveSubscriptions, Is.EqualTo(1));
            Assert.That(_context.Loader.LoadCount, Is.EqualTo(2));
        }

        [Test]
        public async Task PreloadDuringClosingWaitsForOldPreparationCleanup()
        {
            UniTaskCompletionSource preparation = new();
            _context.Presenter.Preparation = preparation;
            _context.Presenter.IgnorePreparationCancellation = true;
            Task<TestPresenter> first = Show();
            _context.Loader.Complete();
            _context.Navigator.Hide<TestPresenter>();
            Task preload = _context.Navigator.PreloadAsync<TestPresenter>().AsTask();
            Assert.That(preload.IsCompleted, Is.False);
            _context.Presenter.Preparation = null;
            preparation.TrySetResult();
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            _context.Loader.Complete();
            await preload;
            Assert.That(_context.Presenter.AfterShowCount, Is.Zero);
            await Show();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public async Task RestoreFromOnDisableStopsOuterSuppression()
        {
            OtherTestPresenter other = await OpenTwoWindows();
            _context.Presenter.View.DisableAction = _context.Navigator.RestoreAll;
            _context.Navigator.SuppressAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);

            _context.Presenter.View.DisableAction = null;
            _context.Navigator.SuppressAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.False);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.False);
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public async Task SuppressFromOnEnablePreservesWindowsStillAwaitingRestore()
        {
            OtherTestPresenter other = await OpenTwoWindows();
            _context.Navigator.SuppressAll();
            _context.Presenter.View.EnableAction = () =>
            {
                _context.Presenter.View.EnableAction = null;
                _context.Navigator.SuppressAll();
            };
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.False);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.False);
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.EqualTo(1));
            Assert.That(other.LiveSubscriptions, Is.EqualTo(1));
        }

        [Test]
        public async Task NestedRestoreAndSuppressKeepsLatestSuppression()
        {
            OtherTestPresenter other = await OpenTwoWindows();
            _context.Presenter.View.DisableAction = () =>
            {
                _context.Presenter.View.DisableAction = null;
                _context.Navigator.RestoreAll();
                _context.Navigator.SuppressAll();
            };
            _context.Navigator.SuppressAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.False);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.False);
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public async Task HideWhileSuppressedDoesNotRestoreClosedWindow()
        {
            OtherTestPresenter other = await OpenTwoWindows();
            _context.Navigator.SuppressAll();
            _context.Navigator.Hide<TestPresenter>();
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View, Is.Null);
            Assert.That(_context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public async Task ShowWhileSuppressedRestoresWithExistingWindow()
        {
            OtherTestPresenter other = new();
            _context.Navigator.RegisterPresenter(other, "OtherView", UILayer.Window, false);
            _context.Loader.CompleteImmediately = true;
            await Show();
            _context.Navigator.SuppressAll();
            await _context.Navigator.ShowAsync<OtherTestPresenter>();
            Assert.That(other.View.gameObject.activeInHierarchy, Is.False);
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public async Task ShowEarlierRegisteredWindowFromOnDisableIsAlsoSuppressed()
        {
            OtherTestPresenter other = new();
            _context.Navigator.RegisterPresenter(other, "OtherView", UILayer.Window, false);
            _context.Loader.CompleteImmediately = true;
            await _context.Navigator.ShowAsync<OtherTestPresenter>();
            Task<TestPresenter> show = null;
            other.View.DisableAction = () => show = Show();
            _context.Navigator.SuppressAll();
            await show;
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.False);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.False);
            other.View.DisableAction = null;
            _context.Navigator.RestoreAll();
            Assert.That(_context.Presenter.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
        }

        private async Task<OtherTestPresenter> OpenTwoWindows()
        {
            OtherTestPresenter other = new();
            _context.Navigator.RegisterPresenter(other, "OtherView", UILayer.Window, false);
            _context.Loader.CompleteImmediately = true;
            await Show();
            await _context.Navigator.ShowAsync<OtherTestPresenter>();
            return other;
        }

        private Task<TestPresenter> Show()
        {
            return _context.Navigator.ShowAsync<TestPresenter>().AsTask();
        }
    }
}
