using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Handles hand-tracking gestures and spatial manipulation for buildable objects and light sources.
    /// Implements pinch-grab, wrist roll rotation, screen-space translation, dual-hand depth translation,
    /// and rapid shake-to-delete gesture detection.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectManipulator : MonoBehaviour
    {
        [Header("Tracking Anchors & Camera")]
        [Tooltip("Right hand pinch point (e.g. midpoint between index finger tip and thumb tip).")]
        [SerializeField] private Transform _rightHandPinchAnchor;

        [Tooltip("Left hand pointing anchor (e.g. index finger tip or wrist facing direction).")]
        [SerializeField] private Transform _leftHandPointAnchor;

        [Tooltip("Main XR/Player Camera.")]
        [SerializeField] private Camera _playerCamera;

        [Header("Depth Translation")]
        [Tooltip("Depth velocity in meters per second when pointing forward/backward.")]
        [SerializeField] private float _depthVelocity = 0.75f;

        [Tooltip("Dot product threshold for pointing direction relative to camera forward.")]
        [SerializeField] private float _pointDotThreshold = 0.8f;

        [Header("Wrist Roll Rotation")]
        [Tooltip("Multiplier for wrist roll angle to object rotation.")]
        [SerializeField] private float _wristRollSensitivity = 1.0f;

        [Header("Shake To Delete Detection")]
        [Tooltip("Minimum speed in m/s to qualify as a shake flick.")]
        [SerializeField] private float _shakeSpeedThreshold = 1.2f;

        [Tooltip("Number of directional reversals required within the time window.")]
        [SerializeField] private int _requiredReversals = 3;

        [Tooltip("Time window in seconds to detect shake reversals.")]
        [SerializeField] private float _shakeWindowDuration = 0.45f;

        [Header("Grab Settings")]
        [Tooltip("Maximum grab detection distance from pinch point.")]
        [SerializeField] private float _grabRadius = 0.15f;
        [SerializeField] private LayerMask _grabbableLayerMask = ~0;

        [Header("Dependencies (Auto-resolved if null)")]
        [SerializeField] private BoundingVolumeManager _boundingVolume;
        [SerializeField] private SurfaceSnapEngine _snapEngine;
        [SerializeField] private BuildUndoRedoManager _undoRedoManager;

        // Current grab state
        private GameObject _grabbedObject;
        private BuildableObject _grabbedBuildable;
        private LightSourceManipulator _grabbedLight;

        private Vector3 _grabHandOffset;
        private Quaternion _grabHandInitialRot;
        private Quaternion _grabObjInitialRot;
        private float _previousWristAngle;

        // Transform history for Undo
        private Vector3 _initialObjPos;
        private Quaternion _initialObjRot;
        private Vector3 _initialObjScale;

        // Shake tracking
        private Vector3 _lastHandPos;
        private Vector3 _lastVelocityDir;
        private readonly List<float> _reversalTimes = new List<float>();

        // Depth control state
        private float _currentDepthOffset = 0f;

        // Events
        public event Action<GameObject> OnObjectGrabbed;
        public event Action<GameObject> OnObjectReleased;
        public event Action<GameObject> OnObjectDeletedByShake;
        public event Action<string> OnManipulationBlocked;

        public bool IsGrabbing => _grabbedObject != null;
        public GameObject GrabbedObject => _grabbedObject;

        /// <summary>
        /// Allows direct depth adjustment (e.g. from editor mouse scroll or gestures).
        /// </summary>
        public void AdjustDepthOffset(float delta)
        {
            _currentDepthOffset += delta;
        }

        private void Awake()
        {
            if (_playerCamera == null) _playerCamera = Camera.main;
            if (_boundingVolume == null) _boundingVolume = FindAnyObjectByType<BoundingVolumeManager>();
            if (_snapEngine == null) _snapEngine = FindAnyObjectByType<SurfaceSnapEngine>();
            if (_undoRedoManager == null) _undoRedoManager = FindAnyObjectByType<BuildUndoRedoManager>();
        }

        private void Update()
        {
            if (_rightHandPinchAnchor == null) return;

            UpdateShakeDetection();

            if (_grabbedObject != null)
            {
                ProcessActiveGrab();
            }
        }

        /// <summary>
        /// Begins grabbing the specified object or probes for one near the pinch anchor.
        /// </summary>
        public bool BeginPinchGrab(GameObject target = null)
        {
            if (_rightHandPinchAnchor == null || Time.timeScale <= 0f) return false;
            if (_grabbedObject != null) ReleasePinchGrab();
            if (target == null)
            {
                target = DetectGrabbableAtPinchPoint();
            }

            if (target == null) return false;

            // Check if it's a light source
            LightSourceManipulator lightManip = target.GetComponentInParent<LightSourceManipulator>();
            if (lightManip != null)
            {
                if (!lightManip.AllowLightMovement)
                {
                    OnManipulationBlocked?.Invoke("This lamp is fixed and cannot be moved.");
                    return false;
                }
                _grabbedLight = lightManip;
                _grabbedObject = lightManip.gameObject;
            }
            else
            {
                _grabbedBuildable = target.GetComponentInParent<BuildableObject>();
                if (_grabbedBuildable == null) return false;
                if (_grabbedBuildable.IsLocked)
                {
                    OnManipulationBlocked?.Invoke("This object is fixed and cannot be moved.");
                    return false;
                }
                _grabbedObject = _grabbedBuildable != null ? _grabbedBuildable.gameObject : target;
            }

            // Record initial state for undo
            _initialObjPos = _grabbedObject.transform.position;
            _initialObjRot = _grabbedObject.transform.rotation;
            _initialObjScale = _grabbedObject.transform.localScale;

            _grabHandOffset = _grabbedObject.transform.position - _rightHandPinchAnchor.position;
            _grabHandInitialRot = _rightHandPinchAnchor.rotation;
            _grabObjInitialRot = _grabbedObject.transform.rotation;
            _previousWristAngle = GetWristRollAngle();
            _currentDepthOffset = 0f;
            _reversalTimes.Clear();
            _lastHandPos = _rightHandPinchAnchor.position;
            _lastVelocityDir = Vector3.zero;

            OnObjectGrabbed?.Invoke(_grabbedObject);
            return true;
        }

        /// <summary>
        /// Releases the currently grabbed object and records the transformation into the undo stack.
        /// </summary>
        public void ReleasePinchGrab()
        {
            if (_grabbedObject == null) return;

            GameObject released = _grabbedObject;
            Vector3 finalPos = released.transform.position;
            Quaternion finalRot = released.transform.rotation;
            Vector3 finalScale = released.transform.localScale;

            // If transformed significantly, record undo command
            if (Vector3.Distance(_initialObjPos, finalPos) > 0.001f ||
                Quaternion.Angle(_initialObjRot, finalRot) > 0.5f)
            {
                if (_undoRedoManager != null)
                {
                    TransformCommand cmd = new TransformCommand(
                        released.transform,
                        _initialObjPos, _initialObjRot, _initialObjScale,
                        finalPos, finalRot, finalScale
                    );
                    _undoRedoManager.RecordCommand(cmd, executeNow: false);
                }
            }

            _grabbedObject = null;
            _grabbedBuildable = null;
            _grabbedLight = null;

            OnObjectReleased?.Invoke(released);
        }

        private void ProcessActiveGrab()
        {
            if (_playerCamera == null || _rightHandPinchAnchor == null) return;

            Transform camTransform = _playerCamera.transform;

            // 1. Dual-Hand Depth Translation
            ProcessDualHandDepth(camTransform);

            // 2. Pinch + Wrist Roll: 1-Axis Rotation
            ProcessWristRollRotation();

            // 3. Pinch + Hand Translation: Screen-space X/Y move
            Vector3 targetPosition = _rightHandPinchAnchor.position + _grabHandOffset + (camTransform.forward * _currentDepthOffset);

            // 4. Calculate object extents for bounds & surface snapping
            Vector3 extents = Vector3.one * 0.15f;
            if (_grabbedBuildable != null)
            {
                extents = _grabbedBuildable.GetExtents();
            }
            else if (_grabbedLight != null)
            {
                extents = _grabbedLight.LightExtents;
            }

            Quaternion targetRotation = _grabbedObject.transform.rotation;

            // 5. Surface & Grid Snapping
            if (_snapEngine != null && _grabbedLight == null)
            {
                SnapResult snapResult = _snapEngine.EvaluateSnap(targetPosition, targetRotation, extents);
                targetPosition = snapResult.SnappedPosition;
                targetRotation = snapResult.SnappedRotation;
            }

            // 6. Hard Boundary Volume Enforcement
            if (_boundingVolume != null)
            {
                targetPosition = _boundingVolume.ClampPosition(targetPosition, extents);
                _boundingVolume.EvaluateProximity(targetPosition, extents);
            }

            // "Gravity Enabled" is an authoring constraint, not Rigidbody simulation.
            // Keep the base on the nearest support, with the build-area floor as a reliable fallback.
            if (_grabbedBuildable != null && _grabbedBuildable.GravityEnabled)
            {
                if (_snapEngine == null || !_snapEngine.TryConstrainToGround(targetPosition, extents, _grabbedBuildable.transform, out Vector3 grounded))
                    grounded = _boundingVolume != null ? _boundingVolume.ConstrainToGround(targetPosition, extents) : targetPosition;
                targetPosition = grounded;
                if (_boundingVolume != null) targetPosition = _boundingVolume.ClampPosition(targetPosition, extents);
            }

            // Apply calculated pose
            if (_grabbedLight != null)
            {
                _grabbedLight.SetPose(targetPosition, targetRotation);
            }
            else
            {
                _grabbedObject.transform.position = targetPosition;
                _grabbedObject.transform.rotation = targetRotation;
            }
        }

        private void ProcessDualHandDepth(Transform camTransform)
        {
            if (_leftHandPointAnchor == null) return;

            Vector3 pointDir = _leftHandPointAnchor.forward;
            float dotCam = Vector3.Dot(pointDir, camTransform.forward);

            // Left Hand Point Forward (dot > 0.8) -> Push Object Away (+Z)
            if (dotCam > _pointDotThreshold)
            {
                _currentDepthOffset += _depthVelocity * Time.deltaTime;
            }
            // Left Hand Point Backward (dot < -0.8) -> Pull Object Closer (-Z)
            else if (dotCam < -_pointDotThreshold)
            {
                _currentDepthOffset -= _depthVelocity * Time.deltaTime;
            }
        }

        private void ProcessWristRollRotation()
        {
            float currentWristAngle = GetWristRollAngle();
            float deltaAngle = Mathf.DeltaAngle(_previousWristAngle, currentWristAngle) * _wristRollSensitivity;
            _previousWristAngle = currentWristAngle;

            if (Mathf.Abs(deltaAngle) > 0.05f)
            {
                // Rotates around local Y-axis
                _grabbedObject.transform.Rotate(Vector3.up, deltaAngle, Space.Self);
            }
        }

        private float GetWristRollAngle()
        {
            if (_rightHandPinchAnchor == null) return 0f;
            // Roll around anchor's forward or camera-aligned axis
            Vector3 right = _rightHandPinchAnchor.right;
            return Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
        }

        private void UpdateShakeDetection()
        {
            if (_rightHandPinchAnchor == null) return;

            Vector3 currentPos = _rightHandPinchAnchor.position;
            float dt = Time.deltaTime;

            if (dt > 0.0001f)
            {
                Vector3 velocity = (currentPos - _lastHandPos) / dt;
                float speed = velocity.magnitude;

                if (speed > _shakeSpeedThreshold)
                {
                    Vector3 currentDir = velocity.normalized;
                    if (_lastVelocityDir != Vector3.zero)
                    {
                        // Direction reversal check
                        if (Vector3.Dot(currentDir, _lastVelocityDir) < -0.5f)
                        {
                            _reversalTimes.Add(Time.time);
                        }
                    }
                    _lastVelocityDir = currentDir;
                }

                // Purge expired reversals
                float cutoffTime = Time.time - _shakeWindowDuration;
                _reversalTimes.RemoveAll(t => t < cutoffTime);

                // Check if threshold reached while actively pinching an object
                if (_reversalTimes.Count >= _requiredReversals && _grabbedObject != null)
                {
                    TriggerShakeDelete();
                }
            }

            _lastHandPos = currentPos;
        }

        private void TriggerShakeDelete()
        {
            if (_grabbedObject == null) return;

            GameObject objToDelete = _grabbedObject;
            _grabbedObject = null;
            _grabbedBuildable = null;
            _grabbedLight = null;
            _reversalTimes.Clear();

            // Record DeleteCommand in undo manager
            if (_undoRedoManager != null)
            {
                DeleteCommand deleteCmd = new DeleteCommand(
                    objToDelete,
                    onDeleted: (go) => go.SetActive(false),
                    onRestored: (go) => go.SetActive(true)
                );
                _undoRedoManager.RecordCommand(deleteCmd, executeNow: true);
            }
            else
            {
                objToDelete.SetActive(false);
            }

            OnObjectDeletedByShake?.Invoke(objToDelete);
        }

        private GameObject DetectGrabbableAtPinchPoint()
        {
            if (_rightHandPinchAnchor == null) return null;

            Collider[] hits = Physics.OverlapSphere(_rightHandPinchAnchor.position, _grabRadius, _grabbableLayerMask);
            if (hits.Length == 0) return null;

            float minDistance = float.MaxValue;
            GameObject closest = null;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].GetComponentInParent<BuildableObject>() == null && hits[i].GetComponentInParent<LightSourceManipulator>() == null) continue;
                float dist = Vector3.Distance(_rightHandPinchAnchor.position, hits[i].ClosestPoint(_rightHandPinchAnchor.position));
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closest = hits[i].gameObject;
                }
            }

            return closest;
        }

        public void ConfigureAnchors(Transform rightPinch, Transform leftPoint, Camera cam)
        {
            _rightHandPinchAnchor = rightPinch;
            _leftHandPointAnchor = leftPoint;
            _playerCamera = cam;
        }
    }
}
