using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Attached to any object that can be spawned, manipulated, and baked in Build Mode.
    /// Manages cached bounds extents, original layers, and interaction states.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildableObject : MonoBehaviour
    {
        [Header("Object Bounds")]
        [SerializeField] private Vector3 _customExtents = Vector3.zero;

        [Header("State")]
        [SerializeField] private bool _isLocked = false;
        [SerializeField] private bool _isMovable = true;

        private int _originalLayer;
        private Collider[] _colliders;
        private Renderer[] _renderers;
        private Rigidbody _rigidbody;

        public bool IsLocked
        {
            get => _isLocked || !_isMovable;
            set => _isLocked = value;
        }

        public bool IsMovable => _isMovable;

        public void SetMovable(bool movable)
        {
            _isMovable = movable;
            if (!movable) _isLocked = false;
        }

        public int OriginalLayer => _originalLayer;

        private void Awake()
        {
            _originalLayer = gameObject.layer;
            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);
            _rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Computes or retrieves oriented bounding extents (rx, ry, rz) for bounding volume calculations.
        /// </summary>
        public Vector3 GetExtents()
        {
            if (_customExtents != Vector3.zero)
            {
                return _customExtents;
            }

            Bounds combined = new Bounds(transform.position, Vector3.zero);
            bool hasBounds = false;

            if (_renderers != null && _renderers.Length > 0)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null) continue;
                    if (!hasBounds)
                    {
                        combined = _renderers[i].bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(_renderers[i].bounds);
                    }
                }
            }
            else if (_colliders != null && _colliders.Length > 0)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] == null) continue;
                    if (!hasBounds)
                    {
                        combined = _colliders[i].bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(_colliders[i].bounds);
                    }
                }
            }

            if (!hasBounds)
            {
                // Fallback default
                return Vector3.one * 0.25f;
            }

            return combined.extents;
        }

        /// <summary>
        /// Freezes physics and interaction during bake or locked state.
        /// </summary>
        public void SetInteractionsEnabled(bool enabled)
        {
            _isLocked = !enabled && _isMovable;

            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = !enabled;
            }

            // Disable/enable non-trigger colliders if needed
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                {
                    _colliders[i].enabled = enabled;
                }
            }
        }

        /// <summary>
        /// Reassigns this object and its children to a specified layer (e.g. ShadowCasters).
        /// </summary>
        public void SetLayerRecursively(int newLayer)
        {
            SetLayerInternal(transform, newLayer);
        }

        /// <summary>
        /// Restores original layers before baking.
        /// </summary>
        public void RestoreOriginalLayer()
        {
            SetLayerInternal(transform, _originalLayer);
        }

        private void SetLayerInternal(Transform current, int layer)
        {
            current.gameObject.layer = layer;
            for (int i = 0; i < current.childCount; i++)
            {
                SetLayerInternal(current.GetChild(i), layer);
            }
        }
    }
}

