using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityMvp.Enums;
using UnityMvp.Interfaces;
using VContainer;

namespace UnityMvp.Support
{
    public class UIRootProvider : MonoBehaviour
    {
        [FormerlySerializedAs("_temporaryRoot")]
        [SerializeField] private Transform temporaryRoot;
        [FormerlySerializedAs("_layers")]
        [SerializeField] private List<UILayerMapping> layers;
        [Tooltip("When enabled, full-screen presenters hide the scene camera by clearing its culling mask.")]
        [SerializeField] private bool disableSceneCameraForFullScreenWindows = true;

        [Inject]
        public void Construct(IUINavigatorInitializable uiNavigator) => uiNavigator.Initialize(GetLayerDictionary(), temporaryRoot, disableSceneCameraForFullScreenWindows);

        private Dictionary<UILayer, Transform> GetLayerDictionary()
        {
            Dictionary<UILayer, Transform> dictionary = new();

            if (layers == null)
                return dictionary;

            foreach (UILayerMapping mapping in layers)
            {
                if (!mapping.Container)
                    continue;

                if (!dictionary.TryAdd(mapping.Layer, mapping.Container))
                    throw new InvalidOperationException($"UI layer {mapping.Layer} is configured more than once.");
            }

            return dictionary;
        }
    }
}
