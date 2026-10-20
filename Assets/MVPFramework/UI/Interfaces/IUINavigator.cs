using System;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace UnityMvp.Interfaces
{
    public interface IUINavigator
    {
        public Observable<IPresenter> OnPresenterClosed { get; }
        public Observable<int> ActiveOverlayWindows { get; }

        public UniTask<T> ShowAsync<T>(Transform parent = null)
            where T : IPresenter;

        public UniTask<T> ShowAsync<T, TData>(TData data, Transform parent = null)
            where T : IPresenter;

        public UniTask ShowAsync(Type type, Transform parent = null);

        public void Hide<T>()
            where T : IPresenter;

        public void Hide(Type type);

        public T Resolve<T>()
            where T : IPresenter;

        public bool IsOpened<T>()
            where T : IPresenter;

        public UniTask PreloadAsync<T>()
            where T : IPresenter;
    }
}
