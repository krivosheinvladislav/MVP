using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityMvp.Interfaces;

namespace UnityMvp.Support
{
    public interface IViewLoader
    {
        public UniTask<TView> LoadViewAsync<TView>(
            string address,
            Transform parent,
            CancellationToken cancellationToken)
            where TView : Component, IView;
        public void UnloadView(string address);
    }
}
