using System;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    public enum SurfaceType
    {
        None,
        Wall,
        Floor,
        Ceiling,
        Furniture
    }

    public enum GridTranslationIncrement
    {
        FiveCentimeters = 0,   // 0.05m
        TenCentimeters = 1,    // 0.10m
        TwentyCentimeters = 2  // 0.20m
    }

    public enum GridRotationIncrement
    {
        Degrees15 = 15,
        Degrees45 = 45,
        Degrees90 = 90
    }

    public struct SnapResult
    {
        public bool IsSnapped;
        public SurfaceType SurfaceType;
        public Vector3 SnappedPosition;
        public Quaternion SnappedRotation;
        public Vector3 SurfaceNormal;
        public RaycastHit HitInfo;
    }

    /// <summary>
    /// Handles real-world room surface snapping (Walls, Floors, Ceilings, Furniture)
    /// and optional incremental translation/rotation grid snapping.
    /// </summary>
    [DisallowMultipleComponent]
    public class SurfaceSnapEngine : MonoBehaviour
    {
        [Header("Room Surface Detection")]
        [Tooltip("Layer mask for room mesh surfaces (spatial scene mesh, room walls, furniture).")]
        [SerializeField] private LayerMask _roomSurfaceLayerMask = ~0;

        [Tooltip("Raycast probe length in meters.")]
        [SerializeField] private float _raycastLength = 0.3f;

        [Tooltip("Maximum distance to surface to trigger snapping.")]
        [SerializeField] private float _snapDistanceThreshold = 0.1f;

        [Tooltip("Maximum allowed angle deviation from cardinal normal to trigger surface snap (degrees).")]
        [SerializeField] private float _cardinalAngleThreshold = 25.0f;

        [Header("Optional Grid Snapping")]
        [SerializeField] private bool _enableGridSnap = false;
        [SerializeField] private GridTranslationIncrement _translationIncrement = GridTranslationIncrement.FiveCentimeters;
        [SerializeField] private GridRotationIncrement _rotationIncrement = GridRotationIncrement.Degrees45;

        [Header("Visual Snap Alignment Helper")]
        [SerializeField] private GameObject _snapHelperVisual;

        // Events
        public event Action<SnapResult> OnSnapStateChanged;

        public bool EnableGridSnap
        {
            get => _enableGridSnap;
            set => _enableGridSnap = value;
        }

        public GridTranslationIncrement TranslationIncrement
        {
            get => _translationIncrement;
            set => _translationIncrement = value;
        }

        public GridRotationIncrement RotationIncrement
        {
            get => _rotationIncrement;
            set => _rotationIncrement = value;
        }

        public float TranslationIncrementMeters => _translationIncrement switch
        {
            GridTranslationIncrement.FiveCentimeters => 0.05f,
            GridTranslationIncrement.TenCentimeters => 0.10f,
            GridTranslationIncrement.TwentyCentimeters => 0.20f,
            _ => 0.05f
        };

        public float RotationIncrementDegrees => (float)_rotationIncrement;

        /// <summary>
        /// Evaluates raycasts against room surfaces along object's cardinal directions and computes snapped pose.
        /// </summary>
        public SnapResult EvaluateSnap(Vector3 candidatePosition, Quaternion candidateRotation, Vector3 extents)
        {
            SnapResult result = new SnapResult
            {
                IsSnapped = false,
                SurfaceType = SurfaceType.None,
                SnappedPosition = candidatePosition,
                SnappedRotation = candidateRotation,
                SurfaceNormal = Vector3.up
            };

            // Cast cardinal rays: Back, Down, Up, Forward, Left, Right
            Vector3[] rayDirections = new Vector3[]
            {
                candidateRotation * Vector3.back,
                Vector3.down,
                Vector3.up,
                candidateRotation * Vector3.forward,
                candidateRotation * Vector3.left,
                candidateRotation * Vector3.right
            };

            float closestDistance = float.MaxValue;
            RaycastHit bestHit = default;
            Vector3 bestRayDir = Vector3.zero;

            for (int i = 0; i < rayDirections.Length; i++)
            {
                Vector3 dir = rayDirections[i].normalized;
                // Raycast origin shifted slightly inside the object
                Vector3 origin = candidatePosition - dir * 0.02f;

                if (Physics.Raycast(origin, dir, out RaycastHit hit, _raycastLength, _roomSurfaceLayerMask))
                {
                    // Check normal alignment with world cardinal axes
                    if (IsAlignedWithCardinalAxis(hit.normal, out Vector3 cardinalAxis))
                    {
                        if (hit.distance < closestDistance && hit.distance <= _snapDistanceThreshold + 0.02f)
                        {
                            closestDistance = hit.distance;
                            bestHit = hit;
                            bestRayDir = dir;
                        }
                    }
                }
            }

            if (closestDistance <= _snapDistanceThreshold + 0.02f)
            {
                result.IsSnapped = true;
                result.HitInfo = bestHit;
                result.SurfaceNormal = bestHit.normal;
                result.SurfaceType = ClassifySurface(bestHit.normal, bestHit.collider);

                // Compute flush position based on surface type
                result.SnappedPosition = CalculateFlushPosition(candidatePosition, candidateRotation, extents, bestHit, result.SurfaceType);

                // Align orientation to target surface if wall or floor
                result.SnappedRotation = CalculateSurfaceRotation(candidateRotation, bestHit.normal, result.SurfaceType);
            }

            // Apply incremental grid snapping if enabled
            if (_enableGridSnap)
            {
                result.SnappedPosition = ApplyGridTranslationSnap(result.SnappedPosition, result.IsSnapped ? bestHit.normal : Vector3.zero);
                result.SnappedRotation = ApplyGridRotationSnap(result.SnappedRotation);
            }

            UpdateVisualHelper(result);
            OnSnapStateChanged?.Invoke(result);
            return result;
        }

        private SurfaceType ClassifySurface(Vector3 normal, Collider collider)
        {
            // Check tags or custom metadata first if available
            if (collider != null)
            {
                string tag = collider.gameObject.tag;
                if (tag.Equals("Wall", StringComparison.OrdinalIgnoreCase)) return SurfaceType.Wall;
                if (tag.Equals("Floor", StringComparison.OrdinalIgnoreCase)) return SurfaceType.Floor;
                if (tag.Equals("Ceiling", StringComparison.OrdinalIgnoreCase)) return SurfaceType.Ceiling;
                if (tag.Equals("Furniture", StringComparison.OrdinalIgnoreCase)) return SurfaceType.Furniture;
            }

            // Normal angle classification
            float dotUp = Vector3.Dot(normal, Vector3.up);
            if (dotUp > 0.7f)
            {
                // Upward facing: Floor or Furniture top
                return (collider != null && collider.name.ToLower().Contains("furniture")) 
                    ? SurfaceType.Furniture 
                    : SurfaceType.Floor;
            }
            if (dotUp < -0.7f)
            {
                return SurfaceType.Ceiling;
            }

            // Vertical normal: Wall
            return SurfaceType.Wall;
        }

        private Vector3 CalculateFlushPosition(Vector3 candidatePos, Quaternion candidateRot, Vector3 extents, RaycastHit hit, SurfaceType surfaceType)
        {
            switch (surfaceType)
            {
                case SurfaceType.Wall:
                {
                    // Flush back face to wall plane, keeping candidate position's planar projection
                    float distanceToPlane = Vector3.Dot(candidatePos - hit.point, hit.normal);
                    float offsetExtent = Mathf.Abs(Vector3.Dot(extents, candidateRot * Vector3.back));
                    if (offsetExtent < 0.01f) offsetExtent = extents.z;

                    // Project onto wall plane with normal offset
                    Vector3 projected = candidatePos - hit.normal * (distanceToPlane - offsetExtent);
                    return projected;
                }

                case SurfaceType.Floor:
                case SurfaceType.Furniture:
                {
                    // Align base to horizontal surface, allowing planar movement
                    float baseOffset = extents.y;
                    return new Vector3(candidatePos.x, hit.point.y + baseOffset, candidatePos.z);
                }

                case SurfaceType.Ceiling:
                {
                    // Align top to ceiling surface
                    float topOffset = extents.y;
                    return new Vector3(candidatePos.x, hit.point.y - topOffset, candidatePos.z);
                }

                default:
                    return hit.point + hit.normal * extents.magnitude * 0.5f;
            }
        }

        private Quaternion CalculateSurfaceRotation(Quaternion currentRot, Vector3 normal, SurfaceType surfaceType)
        {
            if (surfaceType == SurfaceType.Wall)
            {
                // Align back towards wall normal, keeping upright Y orientation
                Vector3 forward = -normal;
                if (forward.sqrMagnitude > 0.001f)
                {
                    return Quaternion.LookRotation(forward, Vector3.up);
                }
            }
            else if (surfaceType == SurfaceType.Floor || surfaceType == SurfaceType.Furniture)
            {
                // Preserve current yaw, flatten pitch and roll
                Vector3 euler = currentRot.eulerAngles;
                return Quaternion.Euler(0f, euler.y, 0f);
            }
            return currentRot;
        }

        public Vector3 ApplyGridTranslationSnap(Vector3 pos, Vector3 constraintNormal)
        {
            float step = TranslationIncrementMeters;
            if (step <= 0f) return pos;

            float x = Mathf.Round(pos.x / step) * step;
            float y = Mathf.Round(pos.y / step) * step;
            float z = Mathf.Round(pos.z / step) * step;

            Vector3 snapped = new Vector3(x, y, z);

            // If snapped to a surface, do not shift off the surface plane
            if (constraintNormal.sqrMagnitude > 0.01f)
            {
                Vector3 delta = snapped - pos;
                Vector3 normalComponent = Vector3.Project(delta, constraintNormal);
                snapped -= normalComponent;
            }

            return snapped;
        }

        public Quaternion ApplyGridRotationSnap(Quaternion rot)
        {
            float step = RotationIncrementDegrees;
            if (step <= 0f) return rot;

            Vector3 euler = rot.eulerAngles;
            float x = Mathf.Round(euler.x / step) * step;
            float y = Mathf.Round(euler.y / step) * step;
            float z = Mathf.Round(euler.z / step) * step;

            return Quaternion.Euler(x, y, z);
        }

        private bool IsAlignedWithCardinalAxis(Vector3 normal, out Vector3 cardinalAxis)
        {
            Vector3[] cardinals = new Vector3[]
            {
                Vector3.up, Vector3.down,
                Vector3.left, Vector3.right,
                Vector3.forward, Vector3.back
            };

            for (int i = 0; i < cardinals.Length; i++)
            {
                if (Vector3.Angle(normal, cardinals[i]) <= _cardinalAngleThreshold)
                {
                    cardinalAxis = cardinals[i];
                    return true;
                }
            }

            cardinalAxis = Vector3.zero;
            return false;
        }

        private void UpdateVisualHelper(SnapResult result)
        {
            if (_snapHelperVisual == null) return;

            if (result.IsSnapped)
            {
                _snapHelperVisual.SetActive(true);
                _snapHelperVisual.transform.position = result.HitInfo.point + result.SurfaceNormal * 0.002f;
                _snapHelperVisual.transform.rotation = Quaternion.LookRotation(result.SurfaceNormal);
            }
            else
            {
                _snapHelperVisual.SetActive(false);
            }
        }
    }
}

