using UnityMvp.Enums;

namespace UnityMvp.Interfaces
{
    public interface IUINavigatorRegistrator
    {
        public void RegisterPresenter(IPresenter presenter, string viewAddress, UILayer layer, bool isFullScreen);

        public void RegisterPresenter<T>(T presenter, string viewAddress, UILayer layer, bool isFullScreen)
            where T : IPresenter;
    }
}
