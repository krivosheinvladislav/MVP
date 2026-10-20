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
using Object = UnityEngine.Object;

namespace UnityMvp.Tests.EditMode
{
    internal sealed class MvpTestContext : IDisposable
    {
        private readonly List<GameObject> _objects = new();

        public MvpTestContext(bool fullScreen = false, int cullingMask = 479)
        {
            Transform root = CreateObject("Root").transform;
            Transform temporaryRoot = CreateObject("TemporaryRoot").transform;
            temporaryRoot.gameObject.SetActive(false);
            Camera = CreateObject("Camera").AddComponent<Camera>();
            Camera.cullingMask = cullingMask;
            Loader = new TestViewLoader(_objects);
            Presenter = new TestPresenter();
            Navigator = new UINavigator(new TestCameraProvider(Camera), Loader);
            Navigator.Initialize(new Dictionary<UILayer, Transform> { [UILayer.Window] = root }, temporaryRoot, fullScreen);
            Navigator.RegisterPresenter(Presenter, "TestView", UILayer.Window, fullScreen);
        }

        public UINavigator Navigator { get; }
        public TestPresenter Presenter { get; }
        public TestViewLoader Loader { get; }
        public Camera Camera { get; }

        public void Dispose()
        {
            Navigator.Dispose();

            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index])
                    Object.DestroyImmediate(_objects[index]);
            }
        }

        private GameObject CreateObject(string name)
        {
            GameObject instance = new(name);
            _objects.Add(instance);
            return instance;
        }

        private sealed class TestCameraProvider : ICameraProvider
        {
            public TestCameraProvider(Camera camera)
            {
                Current = camera;
            }

            public Camera Current { get; }
        }
    }

    internal sealed class TestViewLoader : IViewLoader
    {
        private readonly List<GameObject> _objects;
        private readonly Dictionary<string, UniTaskCompletionSource<GameObject>> _pending = new();
        private readonly Dictionary<string, GameObject> _instances = new();
        private readonly Dictionary<string, Transform> _parents = new();

        public TestViewLoader(List<GameObject> objects)
        {
            _objects = objects;
        }

        public int LoadCount { get; private set; }
        public int UnloadCount { get; private set; }
        public bool ThrowOnUnload { get; set; }
        public bool CompleteImmediately { get; set; }

        public async UniTask<TView> LoadViewAsync<TView>(
            string address,
            Transform parent,
            CancellationToken cancellationToken)
            where TView : Component, IView
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_instances.TryGetValue(address, out GameObject loadedInstance) && loadedInstance)
                return loadedInstance.GetComponent<TView>();

            if (!_pending.TryGetValue(address, out UniTaskCompletionSource<GameObject> completionSource))
            {
                LoadCount++;
                completionSource = new();
                _pending.Add(address, completionSource);
                _parents[address] = parent;

                if (CompleteImmediately)
                    Complete(address);
            }

            GameObject instance = await completionSource.Task.AttachExternalCancellation(cancellationToken);
            return instance ? instance.GetComponent<TView>() : null;
        }

        public void Complete(bool valid = true)
        {
            Complete("TestView", valid);
        }

        public void Complete(string address, bool valid = true)
        {
            UniTaskCompletionSource<GameObject> pending = _pending[address];
            _pending.Remove(address);
            GameObject instance = new(address);
            _instances[address] = instance;
            _objects.Add(instance);
            instance.transform.SetParent(_parents[address], false);
            _parents.Remove(address);

            if (valid)
                instance.AddComponent<TestView>();

            pending.TrySetResult(instance);
        }

        public void UnloadView(string address)
        {
            UnloadCount++;
            _parents.Remove(address);

            if (_pending.Remove(address, out UniTaskCompletionSource<GameObject> pending))
                pending.TrySetCanceled();

            if (_instances.Remove(address, out GameObject instance) && instance)
                Object.DestroyImmediate(instance);

            if (ThrowOnUnload)
                throw new InvalidOperationException("Cleanup failed.");
        }
    }

    [ExecuteAlways]
    internal sealed class TestView : BaseView
    {
        public Action EnableAction { get; set; }
        public Action DisableAction { get; set; }

        private void OnEnable()
        {
            EnableAction?.Invoke();
        }

        private void OnDisable()
        {
            DisableAction?.Invoke();
        }
    }

    internal class TestPresenter : BasePresenter<TestView>, IPayloadReceiver<int>
    {
        public UniTaskCompletionSource Preparation { get; set; }
        public Action BeforeHideAction { get; set; }
        public Action AfterShowAction { get; set; }
        public Action<CompositeDisposable> PreparationAction { get; set; }
        public bool ThrowOnBeforeShow { get; set; }
        public bool IgnorePreparationCancellation { get; set; }
        public CancellationToken ShowToken { get; private set; }
        public int AfterShowCount { get; private set; }
        public int BeforeHideCount { get; private set; }
        public int LiveSubscriptions { get; private set; }
        public int Payload { get; private set; }

        public void ReceivePayload(int data)
        {
            Payload = data;
        }

        public override async UniTask BeforeShow(CompositeDisposable disposables, CancellationToken cancellationToken)
        {
            ShowToken = cancellationToken;
            PreparationAction?.Invoke(disposables);
            LiveSubscriptions++;
            Disposable.Create(() => LiveSubscriptions--).AddTo(disposables);

            if (ThrowOnBeforeShow)
                throw new InvalidOperationException("Preparation failed.");

            if (Preparation != null)
                await (IgnorePreparationCancellation ? Preparation.Task : Preparation.Task.AttachExternalCancellation(cancellationToken));
        }

        public override void AfterShow()
        {
            AfterShowCount++;
            base.AfterShow();
            AfterShowAction?.Invoke();
        }

        public override void BeforeHide()
        {
            BeforeHideCount++;
            BeforeHideAction?.Invoke();
        }
    }

    internal sealed class OtherTestPresenter : TestPresenter
    {
    }
}
