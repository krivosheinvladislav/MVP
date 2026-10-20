using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityMvp.Support;

namespace UnityMvp.Interfaces
{
    public interface IBasePresenter
    {
        public bool IsViewLoaded { get; }
        public Transform Container { get; }

        public void SetViewAddress(string address);

        public UniTask LoadViewAsync(Transform parent, IViewLoader viewLoader, CancellationToken cancellationToken);

        public void UnloadView();
    }
}
