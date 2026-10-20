using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityMvp.Support;

namespace UnityMvp.Tests.EditMode
{
    public sealed class ViewLoaderLifecycleTests
    {
        [Test]
        public void DisposeCancelsEveryPendingPresenterAndSharedWaiter()
        {
            using ViewLoader loader = new();
            AddPendingOperation(loader, "FirstView");
            AddPendingOperation(loader, "SecondView");
            TestPresenter firstPresenter = new();
            OtherTestPresenter secondPresenter = new();
            firstPresenter.SetViewAddress("FirstView");
            secondPresenter.SetViewAddress("SecondView");
            Task first = firstPresenter.LoadViewAsync(null, loader, CancellationToken.None).AsTask();
            Task shared = firstPresenter.LoadViewAsync(null, loader, CancellationToken.None).AsTask();
            Task second = secondPresenter.LoadViewAsync(null, loader, CancellationToken.None).AsTask();
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(shared.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);

            Assert.DoesNotThrow(loader.Dispose);
            Assert.That(first.IsCompleted, Is.True);
            Assert.That(shared.IsCompleted, Is.True);
            Assert.That(second.IsCompleted, Is.True);
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.CatchAsync<OperationCanceledException>(async () => await shared);
            Assert.CatchAsync<OperationCanceledException>(async () => await second);
            Assert.That(firstPresenter.IsViewRequested, Is.False);
            Assert.That(secondPresenter.IsViewRequested, Is.False);
            Assert.That(firstPresenter.View, Is.Null);
            Assert.That(secondPresenter.View, Is.Null);
            Assert.That(GetPendingOperations(loader).Count, Is.Zero);
            Assert.DoesNotThrow(loader.Dispose);
        }

        [Test]
        public void DisposedLoaderRejectsNewLoadsBeforeStartingAddressables()
        {
            using ViewLoader loader = new();
            loader.Dispose();
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await loader.LoadViewAsync<TestView>("View", null, CancellationToken.None));
            Assert.That(GetPendingOperations(loader).Count, Is.Zero);
        }

        private static void AddPendingOperation(ViewLoader loader, string address)
        {
            Type operationType = typeof(ViewLoader).GetNestedType("ViewLoadOperation", BindingFlags.NonPublic);
            Assert.That(operationType, Is.Not.Null);
            object operation = Activator.CreateInstance(operationType, true);
            GetPendingOperations(loader).Add(address, operation);
        }

        private static IDictionary GetPendingOperations(ViewLoader loader)
        {
            FieldInfo operations = typeof(ViewLoader).GetField("_loadingOperations", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(operations, Is.Not.Null);
            return (IDictionary)operations.GetValue(loader);
        }
    }
}
