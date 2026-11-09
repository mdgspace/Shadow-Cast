using System;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Enforces hard bounding volume constraints on all placed and manipulated virtual objects.
    /// Provides boundary proximity feedback and haptic trigger callbacks.
    /// </summary>
    [DisallowMultipleComponent]
    public class BoundingVolumeManager : MonoBehaviour
    {
        [Header("Bounding Volume Geometry")]
        [Tooltip("Center of the bounding envelope in local space.")]
        [SerializeField] private Vector3 _center = Vector3.up * 1.25f;

        [Tooltip("Total dimensions (width, height, depth) of the bounding envelope.")]
        [SerializeField] private Vector3 _size = new Vector3(3.0f, 2.5f, 3.0f);

        [Tooltip("If true, rotates bounds with this transform (OBB). If false, stays world axis-aligned (AABB).")]
        [SerializeField] private bool _useOrientedBounds = false;

        [Header("Boundary Proximity & Feedback")]
        [Tooltip("Proximity distance (in meters) where holographic edge feedback begins to fade in.")]
        [SerializeField] private float _proximityThreshold = 0.15f;

        [Tooltip("Visual renderer/material for boundary feedback (optional).")]
        [SerializeField] private Renderer _boundaryGridRenderer;
        [SerializeField] private string _boundaryAlphaShaderParam = "_GridAlpha";

        [Header("Gizmo Settings")]
        [SerializeField] private bool _drawGizmos = true;
        [SerializeField] private Color _gizmoColor = new Color(0.2f, 0.8f, 1.0f, 0.35f);
        [SerializeField] private Color _gizmoWireColor = new Color(0.2f, 0.8f, 1.0f, 0.9f);

        // Events
        public event Action<Vector3, float> OnBoundaryProximity; // (faceNormal, intensity 0..1)
        public event Action<Vector3> OnBoundaryHit;              // (faceNormal)

        public Vector3 Center => transform.TransformPoint(_center);
        public Vector3 Size => _size;
        public float ProximityThreshold => _proximityThreshold;

        /// <summary>Centers the editable build envelope on the confirmed projection wall.</summary>
        public void ConfigureForWall(Transform wall, float wallWidth, float wallHeight)
        {
            if (wall == null) return;
            transform.SetPositionAndRotation(wall.position, wall.rotation);
            transform.localScale = Vector3.one;
            _center = Vector3.zero;
            _size = new Vector3(Mathf.Max(1f, wallWidth + 1f), Mathf.Max(1f, wallHeight + 1f), 4f);
            _useOrientedBounds = true;
        }

        public Bounds WorldBounds
        {
            get
            {
                if (!_useOrientedBounds)
                {
                    return new Bounds(Center, _size);
                }
                // For OBB, return bounding AABB that encapsulates it
                return new Bounds(Center, Vector3.Scale(_size, transform.lossyScale));
            }
        }

        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Clamps an object's center position within the bounding volume, taking into account
        /// the object's extents (rx, ry, rz).
        /// </summary>
        /// <param name="targetWorldPos">Requested object position.</param>
        /// <param name="objectExtents">Bounding half-size extents of the object.</param>
        /// <returns>Clamped world position.</returns>
        public Vector3 ClampPosition(Vector3 targetWorldPos, Vector3 objectExtents)
        {
            Vector3 center = Center;
            Vector3 halfSize = _size * 0.5f;

            if (_useOrientedBounds)
            {
                // Transform to local space relative to bounding volume
                Vector3 localPos = transform.InverseTransformPoint(targetWorldPos);
                Vector3 localExtents = transform.InverseTransformVector(objectExtents);
                localExtents = new Vector3(Mathf.Abs(localExtents.x), Mathf.Abs(localExtents.y), Mathf.Abs(localExtents.z));

                Vector3 min = _center - halfSize + localExtents;
                Vector3 max = _center + halfSize - localExtents;

                // Protect against objects larger than the volume itself
                if (min.x > max.x) min.x = max.x = _center.x;
                if (min.y > max.y) min.y = max.y = _center.y;
                if (min.z > max.z) min.z = max.z = _center.z;

                Vector3 clampedLocal = new Vector3(
                    Mathf.Clamp(localPos.x, min.x, max.x),
                    Mathf.Clamp(localPos.y, min.y, max.y),
                    Mathf.Clamp(localPos.z, min.z, max.z)
                );

                CheckBoundaryHits(localPos, min, max);
                return transform.TransformPoint(clampedLocal);
            }
            else
            {
                // Axis-aligned bounds
                Vector3 min = (center - halfSize) + objectExtents;
                Vector3 max = (center + halfSize) - objectExtents;

                if (min.x > max.x) min.x = max.x = center.x;
                if (min.y > max.y) min.y = max.y = center.y;
                if (min.z > max.z) min.z = max.z = center.z;

                Vector3 clampedWorld = new Vector3(
                    Mathf.Clamp(targetWorldPos.x, min.x, max.x),
                    Mathf.Clamp(targetWorldPos.y, min.y, max.y),
                    Mathf.Clamp(targetWorldPos.z, min.z, max.z)
                );

                CheckBoundaryHits(targetWorldPos, min, max);
                return clampedWorld;
            }
        }

        /// <summary>Places an object on the bottom face of the configured build area.</summary>
        public Vector3 ConstrainToGround(Vector3 targetWorldPos, Vector3 objectExtents)
        {
            if (_useOrientedBounds)
            {
                Vector3 localPosition = transform.InverseTransformPoint(targetWorldPos);
                Vector3 localExtents = transform.InverseTransformVector(objectExtents);
                localExtents = new Vector3(Mathf.Abs(localExtents.x), Mathf.Abs(localExtents.y), Mathf.Abs(localExtents.z));
                localPosition.y = _center.y - (_size.y * 0.5f) + localExtents.y;
                return transform.TransformPoint(localPosition);
            }

            targetWorldPos.y = Center.y - (_size.y * 0.5f) + objectExtents.y;
            return targetWorldPos;
        }

        /// <summary>
        /// Evaluates proximity of an object to each bounding face.
        /// Activates boundary visual feedback and fires haptic hit events if threshold reached.
        /// </summary>
        public void EvaluateProximity(Vector3 worldPos, Vector3 objectExtents)
        {
            Vector3 localPos = _useOrientedBounds ? transform.InverseTransformPoint(worldPos) : worldPos;
            Vector3 center = _useOrientedBounds ? _center : Center;
            Vector3 halfSize = _size * 0.5f;

            Vector3 min = (center - halfSize) + objectExtents;
            Vector3 max = (center + halfSize) - objectExtents;

            float maxProximity = 0f;
            Vector3 closestNormal = Vector3.zero;

            // Distances to 6 boundary planes
            float distLeft = Mathf.Abs(localPos.x - min.x);
            float distRight = Mathf.Abs(max.x - localPos.x);
            float distBottom = Mathf.Abs(localPos.y - min.y);
            float distTop = Mathf.Abs(max.y - localPos.y);
            float distBack = Mathf.Abs(localPos.z - min.z);
            float distFront = Mathf.Abs(max.z - localPos.z);

            TestFace(distLeft, Vector3.left, ref maxProximity, ref closestNormal);
            TestFace(distRight, Vector3.right, ref maxProximity, ref closestNormal);
            TestFace(distBottom, Vector3.down, ref maxProximity, ref closestNormal);
            TestFace(distTop, Vector3.up, ref maxProximity, ref closestNormal);
            TestFace(distBack, Vector3.back, ref maxProximity, ref closestNormal);
            TestFace(distFront, Vector3.forward, ref maxProximity, ref closestNormal);

            if (_boundaryGridRenderer != null && _propBlock != null)
            {
                _propBlock.SetFloat(_boundaryAlphaShaderParam, maxProximity);
                _boundaryGridRenderer.SetPropertyBlock(_propBlock);
            }

            if (maxProximity > 0f)
            {
                OnBoundaryProximity?.Invoke(closestNormal, maxProximity);
            }
        }

        private void TestFace(float dist, Vector3 normal, ref float maxProximity, ref Vector3 closestNormal)
        {
            if (dist <= _proximityThreshold)
            {
                float intensity = 1.0f - Mathf.Clamp01(dist / _proximityThreshold);
                if (intensity > maxProximity)
                {
                    maxProximity = intensity;
                    closestNormal = _useOrientedBounds ? transform.TransformDirection(normal) : normal;
                }
            }
        }

        private void CheckBoundaryHits(Vector3 currentPos, Vector3 min, Vector3 max)
        {
            const float epsilon = 0.005f;
            if (Mathf.Abs(currentPos.x - min.x) < epsilon) OnBoundaryHit?.Invoke(Vector3.left);
            else if (Mathf.Abs(currentPos.x - max.x) < epsilon) OnBoundaryHit?.Invoke(Vector3.right);

            if (Mathf.Abs(currentPos.y - min.y) < epsilon) OnBoundaryHit?.Invoke(Vector3.down);
            else if (Mathf.Abs(currentPos.y - max.y) < epsilon) OnBoundaryHit?.Invoke(Vector3.up);

            if (Mathf.Abs(currentPos.z - min.z) < epsilon) OnBoundaryHit?.Invoke(Vector3.back);
            else if (Mathf.Abs(currentPos.z - max.z) < epsilon) OnBoundaryHit?.Invoke(Vector3.forward);
        }

        public void SetVolume(Vector3 center, Vector3 size)
        {
            _center = center;
            _size = size;
        }

        private void OnDrawGizmos()
        {
            if (!_drawGizmos) return;

            Gizmos.color = _gizmoColor;
            Matrix4x4 oldMatrix = Gizmos.matrix;

            if (_useOrientedBounds)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(_center, _size);
                Gizmos.color = _gizmoWireColor;
                Gizmos.DrawWireCube(_center, _size);
            }
            else
            {
                Gizmos.DrawCube(Center, _size);
                Gizmos.color = _gizmoWireColor;
                Gizmos.DrawWireCube(Center, _size);
            }

            Gizmos.matrix = oldMatrix;
        }
    }
}

