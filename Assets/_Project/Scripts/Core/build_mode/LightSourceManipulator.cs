using System;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Component attached to the virtual sun or lamp light source.
    /// Provides level developers the ability to enable/disable light movement on a per-level basis.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightSourceManipulator : MonoBehaviour
    {
        [Header("Level Design Configuration")]
        [Tooltip("Developer toggle: enable/disable moving this light source for this level.")]
        [SerializeField] private bool _allowLightMovement = true;

        [Header("Light References")]
        [SerializeField] private Light _lightComponent;
        [SerializeField] private Transform _visualHandle;

        [Header("Light Constraints")]
        [SerializeField] private Vector3 _lightExtents = new Vector3(0.2f, 0.2f, 0.2f);

        // Events
        public event Action<Vector3, Quaternion> OnLightMoved;
        public event Action<bool> OnLightMovementToggled;

        public bool AllowLightMovement
        {
            get => _allowLightMovement;
            set
            {
                if (_allowLightMovement != value)
                {
                    _allowLightMovement = value;
                    OnLightMovementToggled?.Invoke(_allowLightMovement);
                }
            }
        }

        public Light LightComponent => _lightComponent != null ? _lightComponent : (_lightComponent = GetComponentInChildren<Light>());
        public Vector3 LightExtents => _lightExtents;
        public Transform TransformTarget => transform;

        private void Awake()
        {
            if (_lightComponent == null)
            {
                _lightComponent = GetComponentInChildren<Light>();
            }
        }

        /// <summary>
        /// Updates the light position and rotation, firing notification events.
        /// </summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            if (!_allowLightMovement) return;

            transform.position = position;
            transform.rotation = rotation;
            OnLightMoved?.Invoke(position, rotation);
        }

        private void OnValidate()
        {
            if (_visualHandle != null)
            {
                _visualHandle.gameObject.SetActive(_allowLightMovement);
            }
        }
    }
}

