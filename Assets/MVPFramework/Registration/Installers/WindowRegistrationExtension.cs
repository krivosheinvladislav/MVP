using System;
using UnityMvp.Interfaces;
using VContainer;
using VContainer.Unity;

namespace UnityMvp.Registration
{
    public static class WindowRegistrationExtension
    {
        public static RegistrationBuilder AsWindow(this RegistrationBuilder registration)
        {
            return registration.AsSelf().As<IPresenter>();
        }

        public static void RegisterWindows(this IContainerBuilder builder)
        {
            if (builder.Exists(typeof(WindowRegistrationInitializer)))
                throw new InvalidOperationException("Window initialization is already registered in this scope.");

            builder.RegisterEntryPoint<WindowRegistrationInitializer>(Lifetime.Singleton).AsSelf();
        }
    }
}
