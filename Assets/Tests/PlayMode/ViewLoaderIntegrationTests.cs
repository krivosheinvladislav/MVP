using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using UnityMvp.Enums;
using UnityMvp.Infrastructure.Camera;
using UnityMvp.Support;
using Object = UnityEngine.Object;

namespace UnityMvp.Tests.PlayMode
{
    public sealed class ViewLoaderIntegrationTests
    {
        [UnityTest]
        public IEnumerator AddressablesCancelReleaseAndRetry()
        {
            return CheckLoaderAsync().ToCoroutine();
        }

        [UnityTest]
        public IEnumerator MissingAddressDoesNotPreventAnotherWindowFromOpening()
        {
            return CheckMissingAddressAsync().ToCoroutine();
        }

        private static async UniTask CheckLoaderAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            using ViewLoader loader = new();
            UniTask<LoaderTestView> canceled = loader.LoadViewAsync<LoaderTestView>(nameof(LoaderTestView), null, timeout.Token);
            loader.UnloadView(nameof(LoaderTestView));
            await ExpectCanceledAsync(canceled, timeout.Token);

            for (int cycle = 0; cycle < 5; cycle++)
            {
                LoaderTestView view = await loader.LoadViewAsync<LoaderTestView>(nameof(LoaderTestView), null, timeout.Token);
                Assert.That(view, Is.Not.Null);
                loader.UnloadView(nameof(LoaderTestView));
                await UniTask.NextFrame(timeout.Token);
                Assert.That(!view, Is.True);
            }

            using ViewLoader pendingLoader = new();
            UniTask<LoaderTestView> first = pendingLoader.LoadViewAsync<LoaderTestView>(nameof(LoaderTestView), null, timeout.Token);
            UniTask<LoaderTestView> second = pendingLoader.LoadViewAsync<LoaderTestView>("SecondLoaderTestView", null, timeout.Token);
            pendingLoader.Dispose();
            await ExpectCanceledAsync(first, timeout.Token);
            await ExpectCanceledAsync(second, timeout.Token);
            await UniTask.WaitUntil(() => GetInstanceCount() == 0, cancellationToken: timeout.Token);
        }

        private static async UniTask CheckMissingAddressAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            GameObject root = new("NavigationTestRoot");
            Action<AsyncOperationHandle, Exception> originalHandler = ResourceManager.ExceptionHandler;

            try
            {
                Camera camera = root.AddComponent<Camera>();
                int originalMask = camera.cullingMask;
                using ViewLoader loader = new();
                using UINavigator navigator = new(new SceneCameraProvider(camera), loader);
                navigator.Initialize(new Dictionary<UILayer, Transform> { [UILayer.Window] = root.transform }, root.transform, true);
                LoaderTestPresenter valid = new();
                MissingAddressPresenter missing = new();
                navigator.RegisterPresenter(valid, nameof(LoaderTestView), UILayer.Window, false);
                navigator.RegisterPresenter(missing, "MissingTestAddress", UILayer.Window, true);
                Exception reported = null;
                ResourceManager.ExceptionHandler = (_, error) => reported = error;
                bool failed = false;

                try
                {
                    await navigator.ShowAsync<MissingAddressPresenter>().AttachExternalCancellation(timeout.Token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    failed = true;
                    Assert.That(error.ToString(), Does.Contain("MissingTestAddress"));
                }
                finally
                {
                    ResourceManager.ExceptionHandler = originalHandler;
                }

                Assert.That(failed, Is.True);
                Assert.That(reported, Is.Not.Null);
                Assert.That(missing.IsViewLoaded, Is.False);
                Assert.That(missing.IsViewRequested, Is.False);
                Assert.That(navigator.IsOpened<MissingAddressPresenter>(), Is.False);
                Assert.That(camera.cullingMask, Is.EqualTo(originalMask));
                await UniTask.WaitUntil(() => GetInstanceCount() == 0, cancellationToken: timeout.Token);
                await navigator.ShowAsync<LoaderTestPresenter>().AttachExternalCancellation(timeout.Token);
                Assert.That(navigator.IsOpened<LoaderTestPresenter>(), Is.True);
                LoaderTestView view = valid.View;
                navigator.Hide<LoaderTestPresenter>();
                await UniTask.NextFrame(timeout.Token);
                Assert.That(!view, Is.True);
                await UniTask.WaitUntil(() => GetInstanceCount() == 0, cancellationToken: timeout.Token);
            }
            finally
            {
                ResourceManager.ExceptionHandler = originalHandler;
                Object.Destroy(root);
            }
        }

        private static async UniTask ExpectCanceledAsync<T>(UniTask<T> task, CancellationToken token)
        {
            try
            {
                await task;
                Assert.Fail("Released request must cancel.");
            }
            catch (OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
            }
        }

        private static int GetInstanceCount()
        {
            PropertyInfo count = typeof(ResourceManager).GetProperty("InstanceOperationCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(count, Is.Not.Null);
            return (int)count.GetValue(Addressables.ResourceManager);
        }

        private class LoaderTestPresenter : BasePresenter<LoaderTestView>
        {
            public override UniTask BeforeShow(CompositeDisposable disposables, CancellationToken token)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class MissingAddressPresenter : LoaderTestPresenter
        {
        }
    }
}
