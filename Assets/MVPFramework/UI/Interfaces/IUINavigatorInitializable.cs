using System.Collections.Generic;
using UnityEngine;
using UnityMvp.Enums;

namespace UnityMvp.Interfaces
{
    public interface IUINavigatorInitializable
    {
        public void Initialize(
            Dictionary<UILayer, Transform> layerRoots,
            Transform temporaryRoot,
            bool disableSceneCameraForFullScreenWindows);
    }
}
