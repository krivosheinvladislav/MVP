using System;
using System.Collections.Generic;
using System.Reflection;
using UnityMvp.Enums;
using UnityMvp.Interfaces;
using UnityMvp.Support;
using VContainer;
using VContainer.Unity;

namespace UnityMvp.Registration
{
    public sealed class WindowRegistrationInitializer : IInitializable
    {
        private readonly IObjectResolver _resolver;
        private readonly IUINavigatorRegistrator _registrator;

        private bool _isInitialized;

        public WindowRegistrationInitializer(IObjectResolver resolver, IUINavigatorRegistrator registrator)
        {
            _resolver = resolver;
            _registrator = registrator;
        }

        public void Initialize()
        {
            if (_isInitialized)
                return;

            _resolver.TryResolve(typeof(UIRootProvider), out _);
            List<WindowRegistration> windows = GetWindows();
            foreach (WindowRegistration window in windows)
                _registrator.RegisterPresenter(window.Presenter, window.Address, window.Layer, window.IsFullScreen);

            _isInitialized = true;
        }

        private List<WindowRegistration> GetWindows()
        {
            if (!_resolver.TryGetRegistration(typeof(IReadOnlyList<IPresenter>), out VContainer.Registration collection) || collection.Provider is not IEnumerable<VContainer.Registration> registrations)
                throw new InvalidOperationException("Window registrations must use the VContainer presenter collection.");

            List<WindowRegistration> windows = new();
            HashSet<Type> types = new();
            HashSet<string> addresses = new(StringComparer.Ordinal);
            foreach (VContainer.Registration registration in registrations)
            {
                Type type = registration.ImplementationType;
                if (registration.Lifetime == Lifetime.Transient)
                    throw new InvalidOperationException($"Window {type.Name} cannot use Transient lifetime.");
                if (!_resolver.TryGetRegistration(type, out VContainer.Registration selfRegistration) || !ReferenceEquals(selfRegistration, registration))
                    throw new InvalidOperationException($"Window {type.Name} must expose the same registration as itself and IPresenter. Use AsWindow.");

                UIWindowAttribute attribute = type.GetCustomAttribute<UIWindowAttribute>(false);
                if (attribute == null)
                    throw new InvalidOperationException($"Window {type.Name} must declare its own UIWindow attribute.");
                if (!Enum.IsDefined(typeof(UILayer), attribute.Layer))
                    throw new InvalidOperationException($"Window {type.Name} has an invalid UI layer.");

                Type viewType = GetViewType(type);
                if (viewType.IsAbstract || viewType.IsGenericType)
                    throw new InvalidOperationException($"Window {type.Name} must use a concrete non-generic View type.");
                string address = viewType.Name;
                if (!types.Add(type) || !addresses.Add(address))
                    throw new InvalidOperationException($"Duplicate window type or View address in this scope: {type.Name}, {address}.");

                IPresenter presenter = (IPresenter)_resolver.Resolve(registration);
                if (presenter.GetType() != type || !ReferenceEquals(_resolver.Resolve(type), presenter))
                    throw new InvalidOperationException($"Window {type.Name} must resolve to the same concrete DI instance.");
                windows.Add(new WindowRegistration(presenter, address, attribute.Layer, attribute.IsFullScreen));
            }

            return windows;
        }

        private static Type GetViewType(Type presenterType)
        {
            for (Type current = presenterType; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(BasePresenter<>))
                    return current.GetGenericArguments()[0];
            }

            throw new InvalidOperationException($"Window {presenterType.Name} must inherit BasePresenter<TView>.");
        }

        private readonly struct WindowRegistration
        {
            public WindowRegistration(IPresenter presenter, string address, UILayer layer, bool isFullScreen)
            {
                Presenter = presenter;
                Address = address;
                Layer = layer;
                IsFullScreen = isFullScreen;
            }

            public IPresenter Presenter { get; }
            public string Address { get; }
            public UILayer Layer { get; }
            public bool IsFullScreen { get; }
        }
    }
}
