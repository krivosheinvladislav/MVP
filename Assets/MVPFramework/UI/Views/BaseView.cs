using UnityEngine;
using UnityMvp.Interfaces;

namespace UnityMvp
{
    public abstract class BaseView : MonoBehaviour, IView
    {
        public bool IsBeingDestroyed { get; private set; }

        protected virtual void OnDestroy()
        {
            IsBeingDestroyed = true;
        }
    }
}
