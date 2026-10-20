using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using R3;
using UnityMvp.Enums;
using UnityMvp.Interfaces;
using UnityMvp.Registration;
using VContainer;

namespace UnityMvp.Tests.EditMode
{
    public sealed class WindowRegistrationTests
    {
        [TestCase(Lifetime.Scoped)]
        [TestCase(Lifetime.Singleton)]
        public void BootstrapUsesSameDiInstanceAndIsIdempotent(Lifetime lifetime)
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(lifetime).AsWindow();
            using IObjectResolver container = builder.Build();
            WindowRegistrationInitializer initializer = container.Resolve<WindowRegistrationInitializer>();
            initializer.Initialize();
            initializer.Initialize();
            Assert.That(recorder.Windows.Count, Is.EqualTo(1));
            Assert.That(recorder.Windows[0].Presenter, Is.SameAs(container.Resolve<FirstWindow>()));
            Assert.That(recorder.Windows[0].Address, Is.EqualTo(nameof(FirstRegistrationView)));
            Assert.That(recorder.Windows[0].Layer, Is.EqualTo(UILayer.Window));
            Assert.That(recorder.Windows[0].FullScreen, Is.True);
        }

        [Test]
        public void ThirdWindowNeedsNoManualNavigatorRegistration()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(Lifetime.Scoped).AsWindow();
            builder.Register<SecondWindow>(Lifetime.Scoped).AsWindow();
            builder.Register<ThirdWindow>(Lifetime.Scoped).AsWindow();
            using IObjectResolver container = builder.Build();
            container.Resolve<WindowRegistrationInitializer>().Initialize();
            Assert.That(recorder.Windows.Count, Is.EqualTo(3));
            Assert.That(recorder.Windows[2].Presenter, Is.SameAs(container.Resolve<ThirdWindow>()));
            Assert.That(recorder.Windows[2].Address, Is.EqualTo(nameof(ThirdRegistrationView)));
        }

        [TestCase(Lifetime.Scoped)]
        [TestCase(Lifetime.Singleton)]
        public void ChildScopeDoesNotRegisterParentWindows(Lifetime parentLifetime)
        {
            ContainerBuilder builder = CreateBuilder(new RecordingRegistrator());
            builder.Register<FirstWindow>(parentLifetime).AsWindow();
            using IObjectResolver parent = builder.Build();
            RecordingRegistrator childRecorder = new();
            using IScopedObjectResolver child = parent.CreateScope(local =>
            {
                RegisterServices(local, childRecorder);
                local.Register<SecondWindow>(Lifetime.Scoped).AsWindow();
            });
            child.Resolve<WindowRegistrationInitializer>().Initialize();
            Assert.That(childRecorder.Windows.Count, Is.EqualTo(1));
            Assert.That(childRecorder.Windows[0].Presenter, Is.TypeOf<SecondWindow>());
        }

        [Test]
        public void EmptyChildScopeDoesNotConstructParentWindow()
        {
            ContainerBuilder builder = CreateBuilder(new RecordingRegistrator());
            builder.Register<ThrowingParentWindow>(Lifetime.Scoped).AsWindow();
            using IObjectResolver parent = builder.Build();
            RecordingRegistrator recorder = new();
            using IScopedObjectResolver child = parent.CreateScope(local => RegisterServices(local, recorder));
            child.Resolve<WindowRegistrationInitializer>().Initialize();
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void PlainDiRegistrationDoesNotBecomeWindow()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(Lifetime.Scoped);
            using IObjectResolver container = builder.Build();
            container.Resolve<WindowRegistrationInitializer>().Initialize();
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void TransientIsRejectedBeforeConstruction()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<ThrowingParentWindow>(Lifetime.Transient).AsWindow();
            using IObjectResolver container = builder.Build();
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => container.Resolve<WindowRegistrationInitializer>().Initialize());
            Assert.That(error.Message, Does.Contain("Transient"));
            Assert.That(recorder.Windows, Is.Empty);
        }

        [TestCase(typeof(UnmarkedWindow))]
        [TestCase(typeof(InheritedAttributeWindow))]
        [TestCase(typeof(UnbasedWindow))]
        [TestCase(typeof(InvalidLayerWindow))]
        [TestCase(typeof(AbstractViewWindow))]
        [TestCase(typeof(GenericViewWindow))]
        public void InvalidMetadataDoesNotPartiallyRegisterSet(Type invalidType)
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(Lifetime.Scoped).AsWindow();
            builder.Register(invalidType, Lifetime.Scoped).AsWindow();
            using IObjectResolver container = builder.Build();
            Assert.Throws<InvalidOperationException>(() => container.Resolve<WindowRegistrationInitializer>().Initialize());
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void DuplicateViewAddressIsRejected()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(Lifetime.Scoped).AsWindow();
            builder.Register<DuplicateAddressWindow>(Lifetime.Scoped).AsWindow();
            using IObjectResolver container = builder.Build();
            Assert.Throws<InvalidOperationException>(() => container.Resolve<WindowRegistrationInitializer>().Initialize());
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void SameSimpleViewNameInDifferentNamespacesIsRejected()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstCollisionWindow>(Lifetime.Scoped).AsWindow();
            builder.Register<SecondCollisionWindow>(Lifetime.Scoped).AsWindow();
            using IObjectResolver container = builder.Build();
            Assert.Throws<InvalidOperationException>(() => container.Resolve<WindowRegistrationInitializer>().Initialize());
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void DuplicatePresenterRegistrationIsRejected()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(Lifetime.Scoped).AsWindow();
            builder.Register<FirstWindow>(Lifetime.Scoped).AsWindow();
            using IObjectResolver container = builder.Build();
            Assert.Throws<InvalidOperationException>(() => container.Resolve<WindowRegistrationInitializer>().Initialize());
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void DifferentSelfAndInterfaceRegistrationsAreRejected()
        {
            RecordingRegistrator recorder = new();
            ContainerBuilder builder = CreateBuilder(recorder);
            builder.Register<FirstWindow>(Lifetime.Scoped).AsSelf();
            builder.Register<FirstWindow>(Lifetime.Scoped).As<IPresenter>();
            using IObjectResolver container = builder.Build();
            Assert.Throws<InvalidOperationException>(() => container.Resolve<WindowRegistrationInitializer>().Initialize());
            Assert.That(recorder.Windows, Is.Empty);
        }

        [Test]
        public void DuplicateInitializationHelperIsRejected()
        {
            ContainerBuilder builder = new();
            builder.RegisterWindows();
            Assert.Throws<InvalidOperationException>(() => builder.RegisterWindows());
        }

        [Test]
        public void InitializationHelperCanBeRegisteredInChildScope()
        {
            RecordingRegistrator parentRecorder = new();
            ContainerBuilder builder = new();
            builder.RegisterInstance(parentRecorder).As<IUINavigatorRegistrator>();
            builder.Register<FirstWindow>(Lifetime.Scoped).AsWindow();
            builder.RegisterWindows();
            using IObjectResolver parent = builder.Build();
            RecordingRegistrator childRecorder = new();
            using IScopedObjectResolver child = parent.CreateScope(local =>
            {
                local.RegisterInstance(childRecorder).As<IUINavigatorRegistrator>();
                local.Register<SecondWindow>(Lifetime.Scoped).AsWindow();
                local.RegisterWindows();
            });
            Assert.That(parentRecorder.Windows.Count, Is.EqualTo(1));
            Assert.That(childRecorder.Windows.Count, Is.EqualTo(1));
            Assert.That(childRecorder.Windows[0].Presenter, Is.SameAs(child.Resolve<SecondWindow>()));
        }

        private static ContainerBuilder CreateBuilder(RecordingRegistrator recorder)
        {
            ContainerBuilder builder = new();
            RegisterServices(builder, recorder);
            return builder;
        }

        private static void RegisterServices(IContainerBuilder builder, RecordingRegistrator recorder)
        {
            builder.RegisterInstance(recorder).As<IUINavigatorRegistrator>();
            builder.Register<WindowRegistrationInitializer>(Lifetime.Scoped);
        }

        private sealed class RecordingRegistrator : IUINavigatorRegistrator
        {
            public List<(IPresenter Presenter, string Address, UILayer Layer, bool FullScreen)> Windows { get; } = new();

            public void RegisterPresenter<T>(T presenter, string address, UILayer layer, bool fullScreen)
                where T : IPresenter
            {
                RegisterPresenter((IPresenter)presenter, address, layer, fullScreen);
            }

            public void RegisterPresenter(IPresenter presenter, string address, UILayer layer, bool fullScreen)
            {
                Windows.Add((presenter, address, layer, fullScreen));
            }
        }
    }

    public sealed class FirstRegistrationView : BaseView
    {
    }

    public sealed class SecondRegistrationView : BaseView
    {
    }

    public sealed class ThirdRegistrationView : BaseView
    {
    }

    public sealed class GenericRegistrationView<TMarker> : BaseView
    {
    }

    public abstract class RegistrationPresenter<TView> : BasePresenter<TView> where TView : BaseView
    {
        public override UniTask BeforeShow(CompositeDisposable disposables, CancellationToken token)
        {
            return UniTask.CompletedTask;
        }
    }

    [UIWindow(UILayer.Window, IsFullScreen = true)]
    public class FirstWindow : RegistrationPresenter<FirstRegistrationView>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class SecondWindow : RegistrationPresenter<SecondRegistrationView>
    {
    }

    [UIWindow(UILayer.Popup)]
    public sealed class ThirdWindow : RegistrationPresenter<ThirdRegistrationView>
    {
    }

    public sealed class ThrowingParentWindow : FirstWindow
    {
        public ThrowingParentWindow()
        {
            throw new InvalidOperationException("Parent window must not be constructed by a child scope.");
        }
    }

    public sealed class UnmarkedWindow : RegistrationPresenter<SecondRegistrationView>
    {
    }

    public sealed class InheritedAttributeWindow : FirstWindow
    {
    }

    [UIWindow((UILayer)255)]
    public sealed class InvalidLayerWindow : RegistrationPresenter<SecondRegistrationView>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class AbstractViewWindow : RegistrationPresenter<BaseView>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class GenericViewWindow : RegistrationPresenter<GenericRegistrationView<int>>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class DuplicateAddressWindow : RegistrationPresenter<FirstRegistrationView>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class FirstCollisionWindow : RegistrationPresenter<CollisionA.SharedView>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class SecondCollisionWindow : RegistrationPresenter<CollisionB.SharedView>
    {
    }

    [UIWindow(UILayer.Window)]
    public sealed class UnbasedWindow : IPresenter
    {
        public UniTask BeforeShow(CompositeDisposable disposables, CancellationToken token)
        {
            return UniTask.CompletedTask;
        }

        public void AfterShow()
        {
        }

        public void BeforeHide()
        {
        }

        public void AfterHide()
        {
        }
    }
}

namespace UnityMvp.Tests.EditMode.CollisionA
{
    public sealed class SharedView : BaseView
    {
    }
}

namespace UnityMvp.Tests.EditMode.CollisionB
{
    public sealed class SharedView : BaseView
    {
    }
}
