using System;
using UnityEngine;
using UnityMvp.Enums;

namespace UnityMvp.Support
{
    [Serializable]
    public struct UILayerMapping
    {
        public UILayer Layer;
        public Transform Container;
    }
}
