using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace UnityMvp.Interfaces
{
    public interface IPresenter
    {
        public UniTask BeforeShow(CompositeDisposable disposables, CancellationToken cancellationToken);

        public void AfterShow();

        public void BeforeHide();

        public void AfterHide();
    }
}
