using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityMvp.Enums;

namespace UnityMvp.Tests.EditMode
{
    public sealed class UINavigatorRegistrationTests
    {
        [Test]
        public void InvalidRegistrationsDoNotReserveTypesOrAddresses()
        {
            using MvpTestContext context = new();
            OtherTestPresenter presenter = new();
            Assert.Throws<ArgumentNullException>(() => context.Navigator.RegisterPresenter<OtherTestPresenter>(null, "OtherView", UILayer.Window, false));
            Assert.Throws<ArgumentException>(() => context.Navigator.RegisterPresenter(presenter, " ", UILayer.Window, false));
            Assert.Throws<InvalidOperationException>(() => context.Navigator.RegisterPresenter(presenter, "TestView", UILayer.Window, false));
            context.Navigator.RegisterPresenter(presenter, "OtherView", UILayer.Window, false);
            Assert.That(context.Navigator.Resolve<OtherTestPresenter>(), Is.SameAs(presenter));
            Assert.Throws<InvalidOperationException>(() => context.Navigator.RegisterPresenter(new OtherTestPresenter(), "AnotherView", UILayer.Window, false));
        }

        [Test]
        public async Task PayloadCanChangeOnlyBetweenOpenings()
        {
            using MvpTestContext context = new();
            Task<TestPresenter> first = context.Navigator.ShowAsync<TestPresenter, int>(12).AsTask();
            Assert.That(context.Presenter.Payload, Is.EqualTo(12));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await context.Navigator.ShowAsync<TestPresenter, int>(13));
            context.Loader.Complete();
            await first;
            Assert.ThrowsAsync<InvalidOperationException>(async () => await context.Navigator.ShowAsync<TestPresenter, int>(14));
            context.Navigator.Hide<TestPresenter>();
            Task<TestPresenter> second = context.Navigator.ShowAsync<TestPresenter, int>(15).AsTask();
            context.Loader.Complete();
            await second;
            Assert.That(context.Presenter.Payload, Is.EqualTo(15));
        }

        [Test]
        public void IncompatiblePayloadFailsBeforeLoading()
        {
            using MvpTestContext context = new();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await context.Navigator.ShowAsync<TestPresenter, string>("Invalid"));
            Assert.That(context.Loader.LoadCount, Is.Zero);
        }

        [Test]
        public void AddressCannotChangeDuringLoad()
        {
            using MvpTestContext context = new();
            Task preload = context.Navigator.PreloadAsync<TestPresenter>().AsTask();
            Assert.Throws<InvalidOperationException>(() => context.Presenter.SetViewAddress("OtherView"));
            context.Navigator.Hide<TestPresenter>();
            Assert.CatchAsync<OperationCanceledException>(async () => await preload);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RegisteredAddressCannotChangeBeforeLoadOrAfterHide(bool closeFirst)
        {
            using MvpTestContext context = new();
            OtherTestPresenter other = new();
            context.Navigator.RegisterPresenter(other, "OtherView", UILayer.Window, false);
            context.Loader.CompleteImmediately = true;

            if (closeFirst)
            {
                await context.Navigator.ShowAsync<TestPresenter>();
                context.Navigator.Hide<TestPresenter>();
            }

            Assert.Throws<InvalidOperationException>(() => context.Presenter.SetViewAddress("OtherView"));
            Assert.DoesNotThrow(() => context.Presenter.SetViewAddress("TestView"));
            await context.Navigator.ShowAsync<TestPresenter>();
            await context.Navigator.ShowAsync<OtherTestPresenter>();
            Assert.That(context.Presenter.View, Is.Not.SameAs(other.View));
            context.Navigator.Hide<TestPresenter>();
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
            Assert.That(other.LiveSubscriptions, Is.EqualTo(1));
            Assert.That(context.Navigator.IsOpened<OtherTestPresenter>(), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ThrowingDisposableDoesNotPreventRemainingCleanup(bool disposeNavigator)
        {
            using MvpTestContext context = new();
            int throwingDisposeCount = 0;
            context.Presenter.PreparationAction = disposables => Disposable.Create(() =>
            {
                throwingDisposeCount++;
                throw new InvalidOperationException("Subscription cleanup failed.");
            }).AddTo(disposables);
            context.Loader.CompleteImmediately = true;
            await context.Navigator.ShowAsync<TestPresenter>();
            LogAssert.Expect(LogType.Exception, new Regex("Subscription cleanup failed"));

            if (disposeNavigator)
                context.Navigator.Dispose();
            else
                context.Navigator.Hide<TestPresenter>();

            Assert.That(throwingDisposeCount, Is.EqualTo(1));
            Assert.That(context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(context.Presenter.View, Is.Null);

            if (!disposeNavigator)
            {
                context.Presenter.PreparationAction = null;
                await context.Navigator.ShowAsync<TestPresenter>();
                context.Navigator.Hide<TestPresenter>();
                Assert.That(context.Presenter.LiveSubscriptions, Is.Zero);
                Assert.That(throwingDisposeCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void ThrowingDisposablePreservesPreparationErrorForAllWaiters()
        {
            using MvpTestContext context = new();
            context.Presenter.ThrowOnBeforeShow = true;
            context.Presenter.PreparationAction = disposables => Disposable.Create(() => throw new InvalidOperationException("Subscription cleanup failed.")).AddTo(disposables);
            Task<TestPresenter> first = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            Task<TestPresenter> second = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            LogAssert.Expect(LogType.Exception, new Regex("Subscription cleanup failed"));
            context.Loader.Complete();
            InvalidOperationException firstError = Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
            InvalidOperationException secondError = Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
            Assert.That(firstError.Message, Is.EqualTo("Preparation failed."));
            Assert.That(secondError, Is.SameAs(firstError));
            Assert.That(context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(context.Presenter.View, Is.Null);
        }

        [Test]
        public async Task MultipleFullscreenWindowsKeepCameraDisabledUntilLastHide()
        {
            using MvpTestContext context = new(true);
            OtherTestPresenter other = new();
            context.Navigator.RegisterPresenter(other, "OtherView", UILayer.Window, true);
            Task<TestPresenter> first = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            Task<OtherTestPresenter> second = context.Navigator.ShowAsync<OtherTestPresenter>().AsTask();
            context.Loader.Complete("OtherView");
            await second;
            context.Loader.Complete();
            await first;
            context.Navigator.Hide<TestPresenter>();
            Assert.That(context.Camera.cullingMask, Is.Zero);
            Assert.That(other.View.gameObject.activeInHierarchy, Is.True);
            context.Navigator.Hide<OtherTestPresenter>();
            Assert.That(context.Camera.cullingMask, Is.EqualTo(479));
            Assert.That(context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(other.LiveSubscriptions, Is.Zero);
        }

        [Test]
        public void CleanupFailureDoesNotMaskSharedLoadFailure()
        {
            using MvpTestContext context = new();
            context.Loader.ThrowOnUnload = true;
            Task first = context.Presenter.LoadViewAsync(null, context.Loader, CancellationToken.None).AsTask();
            Task second = context.Presenter.LoadViewAsync(null, context.Loader, CancellationToken.None).AsTask();
            LogAssert.Expect(LogType.Exception, new Regex("Cleanup failed"));
            context.Loader.Complete(false);
            InvalidOperationException firstError = Assert.ThrowsAsync<InvalidOperationException>(async () => await first);
            InvalidOperationException secondError = Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
            Assert.That(secondError, Is.SameAs(firstError));
            Assert.That(firstError.Message, Does.Contain("was not loaded"));
            Assert.That(context.Presenter.IsViewRequested, Is.False);
            context.Loader.ThrowOnUnload = false;
        }

        [Test]
        public async Task ReopenFromCounterObserverSurvivesOldAttemptCompletion()
        {
            using MvpTestContext context = new();
            Task<TestPresenter> reopen = null;
            bool shouldReopen = true;
            using IDisposable subscription = context.Navigator.ActiveOverlayWindows.Subscribe(count =>
            {
                if (count != 1 || !shouldReopen)
                    return;

                shouldReopen = false;
                context.Navigator.Hide<TestPresenter>();
                reopen = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            });
            Task<TestPresenter> first = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            context.Loader.Complete();
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.That(context.Loader.LoadCount, Is.EqualTo(2));
            context.Loader.Complete();
            await reopen;
            Assert.That(context.Navigator.IsOpened<TestPresenter>(), Is.True);
            Assert.That(context.Presenter.LiveSubscriptions, Is.EqualTo(1));
        }

        [TestCase(479)]
        [TestCase(-1)]
        public async Task FullScreenRestoresOriginalCameraMask(int cullingMask)
        {
            using MvpTestContext context = new(true, cullingMask);
            Task<TestPresenter> show = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            context.Loader.Complete();
            await show;
            Assert.That(context.Camera.cullingMask, Is.Zero);
            context.Navigator.Hide<TestPresenter>();
            Assert.That(context.Camera.cullingMask, Is.EqualTo(cullingMask));
            show = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            context.Loader.Complete();
            await show;
            context.Navigator.Dispose();
            Assert.That(context.Camera.cullingMask, Is.EqualTo(cullingMask));
        }

        [Test]
        public void MissingUsedLayerFailsAndReleasesPreparation()
        {
            using MvpTestContext context = new();
            context.Navigator.Initialize(new Dictionary<UILayer, Transform>(), null, false);
            Task<TestPresenter> show = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            context.Loader.Complete();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await show);
            Assert.That(context.Presenter.LiveSubscriptions, Is.Zero);
            Assert.That(context.Presenter.ShowToken.IsCancellationRequested, Is.True);
            Assert.That(context.Presenter.View, Is.Null);
        }

        [Test]
        public async Task LatePreparationCannotActivateCanceledAttempt()
        {
            using MvpTestContext context = new();
            UniTaskCompletionSource preparation = new();
            context.Presenter.Preparation = preparation;
            context.Presenter.IgnorePreparationCancellation = true;
            Task<TestPresenter> first = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            context.Loader.Complete();
            context.Navigator.Hide<TestPresenter>();
            Task<TestPresenter> second = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(context.Loader.LoadCount, Is.EqualTo(1));
            context.Presenter.Preparation = null;
            preparation.TrySetResult();
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.That(context.Presenter.AfterShowCount, Is.Zero);
            context.Loader.Complete();
            await second;
            Assert.That(context.Presenter.LiveSubscriptions, Is.EqualTo(1));
        }

        [Test]
        public async Task ClosingEventPrecedesQueuedReopen()
        {
            using MvpTestContext context = new();
            Task<TestPresenter> first = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            context.Loader.Complete();
            await first;
            context.Loader.CompleteImmediately = true;
            Task<TestPresenter> reopen = null;
            context.Presenter.BeforeHideAction = () => reopen = context.Navigator.ShowAsync<TestPresenter>().AsTask();
            bool? wasOpenDuringClosingEvent = null;
            using IDisposable subscription = context.Navigator.OnPresenterClosed.Subscribe(_ => wasOpenDuringClosingEvent = context.Navigator.IsOpened<TestPresenter>());
            context.Navigator.Hide<TestPresenter>();
            context.Presenter.BeforeHideAction = null;
            await reopen;
            Assert.That(wasOpenDuringClosingEvent, Is.False);
            Assert.That(context.Navigator.IsOpened<TestPresenter>(), Is.True);
            Assert.That(context.Presenter.LiveSubscriptions, Is.EqualTo(1));
        }
    }
}
