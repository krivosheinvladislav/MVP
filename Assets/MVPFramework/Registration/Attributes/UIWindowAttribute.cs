using System;
using UnityMvp.Enums;

namespace UnityMvp.Registration
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class UIWindowAttribute : Attribute
    {
        public UIWindowAttribute(UILayer layer)
        {
            Layer = layer;
        }

        public UILayer Layer { get; }
        public bool IsFullScreen { get; set; }
    }
}
