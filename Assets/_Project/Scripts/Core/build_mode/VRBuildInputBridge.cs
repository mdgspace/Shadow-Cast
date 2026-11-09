using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>
    /// Bridges Meta Quest 3 Hand-Tracking / Controllers and Editor Desktop Input
    /// directly to the Build Mode manipulation systems.
    /// Handles pinch-grab triggering, haptic feedback on boundary collision,
    /// wrist tap undo/redo, and complete interactive editor simulation with debug HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public class VRBuildInputBridge : MonoBehaviour
    {
        [Header("Manual Overrides (Leave empty to auto-detect)")]
        [Tooltip("Right hand pinch anchor or controller transform. Auto-detected from VR rig if unassigned.")]
        [SerializeField] private Transform _rightPinchAnchor;

        [Tooltip("Left hand pointing anchor or controller transform. Auto-detected from VR rig if unassigned.")]
        [SerializeField] private Transform _leftPointAnchor;

        [Tooltip("XR Headset Camera. Auto-detected from Camera.main if unassigned.")]
        [SerializeField] private Camera _headsetCamera;

        [Header("Editor & Non-VR Emulation")]
        [Tooltip("Enables mouse, keyboard, and debug HUD emulation when running in the Unity Editor or non-VR.")]
        [SerializeField] private bool _enableEditorSimulation = true;
        [SerializeField] private bool _showDebugGUI = true;

        [Header("Haptics Configuration")]
        [SerializeField] private float _hapticAmplitude = 0.5f;
        [SerializeField] private float _hapticDuration = 0.08f;

        // Internal dependencies (auto-resolved)
        private BuildModeManager _buildModeManager;
        private ObjectManipulator _manipulator;
        private BoundingVolumeManager _boundingVolume;
        private BuildUndoRedoManager _undoRedo;

        // Input state
        private bool _wasPinching = false;
        private UnityEngine.XR.InputDevice _rightDevice;
        private UnityEngine.XR.InputDevice _leftDevice;
        private bool _devicesInitialized = false;

        // Editor simulation virtual pinch anchor
        private GameObject _simulatedRightAnchor;
        private GameObject _simulatedLeftAnchor;
        private float _editorGrabDistance = 1.5f;
        private Vector3 _lastMousePos;

        private void Awake()
        {
            _buildModeManager = GetComponent<BuildModeManager>() ?? FindAnyObjectByType<BuildModeManager>();
            if (_buildModeManager != null)
            {
                _manipulator = _buildModeManager.Manipulator;
                _boundingVolume = _buildModeManager.BoundingVolume;
                _undoRedo = _buildModeManager.UndoRedoManager;
            }

            if (_manipulator == null) _manipulator = GetComponentInChildren<ObjectManipulator>() ?? FindAnyObjectByType<ObjectManipulator>();
            if (_boundingVolume == null) _boundingVolume = GetComponentInChildren<BoundingVolumeManager>() ?? FindAnyObjectByType<BoundingVolumeManager>();
            if (_undoRedo == null) _undoRedo = GetComponentInChildren<BuildUndoRedoManager>() ?? FindAnyObjectByType<BuildUndoRedoManager>();

            AutoDetectRig();
        }

        private void Start()
        {
            if (Application.isMobilePlatform || XRSettings.isDeviceActive)
            {
                _enableEditorSimulation = false;
                _showDebugGUI = false;
            }
            if (_boundingVolume != null)
            {
                _boundingVolume.OnBoundaryHit += HandleBoundaryHit;
            }

            InitializeXRDevices();
        }

        private void OnDestroy()
        {
            if (_boundingVolume != null)
            {
                _boundingVolume.OnBoundaryHit -= HandleBoundaryHit;
            }
        }

        private void AutoDetectRig()
        {
            if (_headsetCamera == null)
            {
                _headsetCamera = Camera.main;
            }

            // Attempt to locate Meta Quest or OpenXR hand/controller anchors
            if (_rightPinchAnchor == null)
            {
                _rightPinchAnchor = FindAnchorByNames("RightHandAnchor", "RightControllerAnchor", "RightHandDirectPinchProxy", "RightHand");
            }

            if (_leftPointAnchor == null)
            {
                _leftPointAnchor = FindAnchorByNames("LeftHandAnchor", "LeftControllerAnchor", "LeftHandDirectPinchProxy", "LeftHand");
            }

            // In Editor or Non-VR desktop mode, setup simulated anchors
            if ((_rightPinchAnchor == null || _leftPointAnchor == null) && _enableEditorSimulation)
            {
                SetupEditorSimulationAnchors();
            }

            if (_manipulator != null && _rightPinchAnchor != null && _leftPointAnchor != null)
            {
                _manipulator.ConfigureAnchors(_rightPinchAnchor, _leftPointAnchor, _headsetCamera);
            }
        }

        /// <summary>Uses live XR controller poses as movable hand anchors when the rig has no named hand transforms.</summary>
        public void ConfigureInputAnchors(Transform rightPinchAnchor, Transform leftPointAnchor, Camera headsetCamera)
        {
            if (rightPinchAnchor == null) return;
            _rightPinchAnchor = rightPinchAnchor;
            if (leftPointAnchor != null) _leftPointAnchor = leftPointAnchor;
            if (headsetCamera != null) _headsetCamera = headsetCamera;
            _manipulator?.ConfigureAnchors(_rightPinchAnchor, _leftPointAnchor, _headsetCamera);
        }

        private Transform FindAnchorByNames(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                GameObject obj = GameObject.Find(names[i]);
                if (obj != null) return obj.transform;
            }
            return null;
        }

        private void SetupEditorSimulationAnchors()
        {
            Transform camTransform = _headsetCamera != null ? _headsetCamera.transform : transform;

            if (_rightPinchAnchor == null)
            {
                _simulatedRightAnchor = new GameObject("[Simulated_RightPinchAnchor]");
                _simulatedRightAnchor.transform.SetParent(camTransform);
                _simulatedRightAnchor.transform.localPosition = new Vector3(0.2f, -0.15f, 0.5f);
                _rightPinchAnchor = _simulatedRightAnchor.transform;
            }

            if (_leftPointAnchor == null)
            {
                _simulatedLeftAnchor = new GameObject("[Simulated_LeftPointAnchor]");
                _simulatedLeftAnchor.transform.SetParent(camTransform);
                _simulatedLeftAnchor.transform.localPosition = new Vector3(-0.2f, -0.15f, 0.5f);
                _leftPointAnchor = _simulatedLeftAnchor.transform;
            }
        }

        private void InitializeXRDevices()
        {
            var rightDevices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightDevices);
            if (rightDevices.Count > 0) _rightDevice = rightDevices[0];

            var leftDevices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, leftDevices);
            if (leftDevices.Count > 0) _leftDevice = leftDevices[0];

            _devicesInitialized = true;
        }

        private void Update()
        {
            if (!_devicesInitialized || !_rightDevice.isValid || !_leftDevice.isValid)
            {
                InitializeXRDevices();
            }

            // Build manipulation, including light grabs, is unavailable while the player is using the arcade controls.
            if (_buildModeManager != null && (!_buildModeManager.IsBuildMode ||
                (_buildModeManager.BakePipeline != null && _buildModeManager.BakePipeline.CurrentState == BakeState.Baking)))
            {
                if (_wasPinching) _manipulator?.ReleasePinchGrab();
                _wasPinching = false;
                return;
            }

            bool isPinching = PollPinchState();

            // Detect Pinch Edge Trigger
            if (isPinching && !_wasPinching)
            {
                _manipulator?.BeginPinchGrab();
            }
            else if (!isPinching && _wasPinching)
            {
                _manipulator?.ReleasePinchGrab();
            }

            _wasPinching = isPinching;

            // Poll VR Controller Buttons / Wrist Taps for Undo/Redo
            PollSecondaryGestures();

            // Editor / Non-VR testing simulation
            if (_enableEditorSimulation)
            {
                UpdateEditorSimulation();
            }
        }

        private bool PollPinchState()
        {
            try
            {
                if (OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                    OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch) ||
                    OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) ||
                    OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RHand))
                {
                    return true;
                }
            }
            catch { }

            if (_rightDevice.isValid)
            {
                if (_rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigger) && trigger)
                {
                    return true;
                }
                if (_rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip)
                {
                    return true;
                }
            }

            return false;
        }

        private void PollSecondaryGestures()
        {
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.LTouch) ||
                    OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch))
                {
                    _undoRedo?.Undo();
                }

                if (OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch))
                {
                    _undoRedo?.Redo();
                }
            }
            catch { }

            if (_leftDevice.isValid)
            {
                if (_leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool btn) && btn)
                {
                    _undoRedo?.Undo();
                }
            }
        }

        private void UpdateEditorSimulation()
        {
            if (_headsetCamera == null || Mouse.current == null) return;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            Vector2 mousePosition = mouse.position.ReadValue();

            // Toggle HUD overlay
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
            {
                _showDebugGUI = !_showDebugGUI;
            }

            // 1. Mouse Left Click: Raycast to Grab & Move
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Ray ray = _headsetCamera.ScreenPointToRay(mousePosition);
                GameObject hitTarget = null;

                if (Physics.Raycast(ray, out RaycastHit hit, 50f))
                {
                    hitTarget = hit.collider.gameObject;
                    _editorGrabDistance = hit.distance;
                    if (_simulatedRightAnchor != null)
                    {
                        _simulatedRightAnchor.transform.position = hit.point;
                    }
                }
                else
                {
                    _editorGrabDistance = 1.5f;
                    if (_simulatedRightAnchor != null)
                    {
                        _simulatedRightAnchor.transform.position = ray.GetPoint(_editorGrabDistance);
                    }
                }

                _manipulator?.BeginPinchGrab(hitTarget);
                _wasPinching = true;
            }
            else if (mouse.leftButton.isPressed && _simulatedRightAnchor != null)
            {
                // Dragging in screen space
                Ray ray = _headsetCamera.ScreenPointToRay(mousePosition);
                _simulatedRightAnchor.transform.position = ray.GetPoint(_editorGrabDistance);

                // Right click while dragging to rotate around camera forward
                if (mouse.rightButton.isPressed)
                {
                    float mouseDeltaX = mouse.delta.ReadValue().x;
                    _simulatedRightAnchor.transform.Rotate(Vector3.forward, -mouseDeltaX * 8.0f, Space.Self);
                }
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                _manipulator?.ReleasePinchGrab();
                _wasPinching = false;
            }

            // 2. Mouse Scroll Wheel: Depth translation (+Z / -Z)
            float scroll = mouse.scroll.ReadValue().y * 0.001f;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                _editorGrabDistance = Mathf.Clamp(_editorGrabDistance + scroll * 1.5f, 0.3f, 10f);
                _manipulator?.AdjustDepthOffset(scroll * 1.5f);
            }

            // 3. Hotkeys
            if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.digit1Key.wasPressedThisFrame))
            {
                SpawnEditorTestObject();
            }

            if (keyboard != null && keyboard.zKey.wasPressedThisFrame) _buildModeManager?.Undo();
            if (keyboard != null && keyboard.yKey.wasPressedThisFrame) _buildModeManager?.Redo();
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame) _buildModeManager?.ToggleGridSnap();
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame) _buildModeManager?.ToggleMode();
            if (keyboard != null && (keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame)) _buildModeManager?.DeleteSelected();

            _lastMousePos = mousePosition;
        }

        private void SpawnEditorTestObject()
        {
            if (_buildModeManager == null) return;

            if (_buildModeManager.BuildablePrefabs != null && _buildModeManager.BuildablePrefabs.Count > 0)
            {
                _buildModeManager.SpawnPrefabIndex(0);
            }
            else
            {
                // Create a standalone default cube primitive
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "TestPlatformCube";
                cube.transform.localScale = new Vector3(0.5f, 0.2f, 0.3f);

                Vector3 spawnPos = _headsetCamera != null 
                    ? _headsetCamera.transform.position + _headsetCamera.transform.forward * 1.2f 
                    : Vector3.forward * 1.2f;

                _buildModeManager.SpawnObject(cube, spawnPos);
            }
        }

        private void HandleBoundaryHit(Vector3 normal)
        {
            TriggerHapticPulse();
        }

        public void TriggerHapticPulse()
        {
            try
            {
                OVRInput.SetControllerVibration(_hapticAmplitude, _hapticAmplitude, OVRInput.Controller.RTouch);
                StartCoroutine(StopHapticsAfterDuration(_hapticDuration));
            }
            catch { }

            if (_rightDevice.isValid)
            {
                if (_rightDevice.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
                {
                    _rightDevice.SendHapticImpulse(0, _hapticAmplitude, _hapticDuration);
                }
            }
        }

        private IEnumerator StopHapticsAfterDuration(float duration)
        {
            yield return new WaitForSeconds(duration);
            try
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            }
            catch { }
        }

        private void OnGUI()
        {
            if (!_showDebugGUI || !_enableEditorSimulation) return;

            GUILayout.BeginArea(new Rect(15, 15, 270, 290), "AR Shadow Build Mode (Editor HUD)", GUI.skin.window);

            string modeText = _buildModeManager != null && _buildModeManager.IsPlayMode ? "<color=green>PLAY MODE</color>" : "<color=cyan>BUILD MODE</color>";
            GUILayout.Label($"<b>Status:</b> {modeText}", new GUIStyle(GUI.skin.label) { richText = true });

            if (_buildModeManager != null)
            {
                GUILayout.Label($"Objects Active: {_buildModeManager.ActiveObjects.Count}");
                GUILayout.Label($"Selected: {(_buildModeManager.SelectedObject != null ? _buildModeManager.SelectedObject.name : "None")}");
            }

            GUILayout.Space(6);

            if (GUILayout.Button("[Space] Spawn Test Cube"))
            {
                SpawnEditorTestObject();
            }

            if (GUILayout.Button("[B] " + (_buildModeManager != null && _buildModeManager.IsPlayMode ? "Return to Build Mode" : "Bake Shadows & Play")))
            {
                _buildModeManager?.ToggleMode();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("[Z] Undo")) _buildModeManager?.Undo();
            if (GUILayout.Button("[Y] Redo")) _buildModeManager?.Redo();
            GUILayout.EndHorizontal();

            if (GUILayout.Button("[G] Toggle Grid Snap"))
            {
                _buildModeManager?.ToggleGridSnap();
            }

            if (GUILayout.Button("[Del] Delete Selected"))
            {
                _buildModeManager?.DeleteSelected();
            }

            GUILayout.Space(4);
            GUILayout.Label("<size=10><b>Controls:</b>\n• Left-Click + Drag: Move\n• Right-Click + Drag: Rotate\n• Scroll Wheel: Depth (Push/Pull)\n• F1: Toggle this HUD</size>", new GUIStyle(GUI.skin.label) { richText = true });

            GUILayout.EndArea();
        }
    }
}
