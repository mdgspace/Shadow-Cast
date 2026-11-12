using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Oculus.Interaction.Input;
using HandFinger = Oculus.Interaction.Input.HandFinger;
using ShadowCast.Core.Campaign;

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

        // Internal dependencies (auto-resolved)
        private BuildModeManager _buildModeManager;
        private ObjectManipulator _manipulator;
        private BoundingVolumeManager _boundingVolume;
        private BuildUndoRedoManager _undoRedo;

        // Input state
        private bool _wasPinching = false;

        // Editor simulation virtual pinch anchor
        private GameObject _simulatedRightAnchor;
        private GameObject _simulatedLeftAnchor;
        private float _editorGrabDistance = 1.5f;
        private Vector3 _lastMousePos;
        private ShadowCastHeadsetUI _campaignUI;
        private CampaignFlowController _campaignFlow;
        private bool _hasTrackedAnchor;
        private bool _waitForGrabRelease;

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
            // Required components can Awake before BuildModeManager has assigned its fields.
            _manipulator = _buildModeManager != null ? _buildModeManager.Manipulator : _manipulator;
            _undoRedo = _buildModeManager != null ? _buildModeManager.UndoRedoManager : _undoRedo;
            _campaignUI = FindAnyObjectByType<ShadowCastHeadsetUI>();
            _campaignFlow = FindAnyObjectByType<CampaignFlowController>();
            AutoDetectRig();
            if (_boundingVolume != null)
            {
                _boundingVolume.OnBoundaryHit += HandleBoundaryHit;
            }


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
                _rightPinchAnchor = FindAnchorByNames("RightHandDirectPinchProxy", "RightHandAnchor", "RightHand");
            }

            if (_leftPointAnchor == null)
            {
                _leftPointAnchor = FindAnchorByNames("LeftHandDirectPinchProxy", "LeftHandAnchor", "LeftHand");
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
            _hasTrackedAnchor = rightPinchAnchor != null;
            if (rightPinchAnchor == null) return;
            _rightPinchAnchor = rightPinchAnchor;
            _leftPointAnchor = leftPointAnchor;
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

        private bool undoWasDown, redoWasDown;
        private void Update()
        {
            if (Time.timeScale <= 0f || (_campaignUI != null && _campaignUI.BlocksBuildInput) ||
                (_buildModeManager != null && (!_buildModeManager.IsBuildMode ||
                (_buildModeManager.BakePipeline != null && _buildModeManager.BakePipeline.CurrentState == BakeState.Baking)))
                )
            {
                if (_wasPinching) _manipulator?.ReleasePinchGrab();
                _wasPinching = false;
                _waitForGrabRelease = true;
                return;
            }
            if (_enableEditorSimulation && !XRSettings.isDeviceActive)
            {
                // Do not run VR release detection over the desktop drag state.
                if (_simulatedRightAnchor == null) { _rightPinchAnchor = null; SetupEditorSimulationAnchors(); }
                _rightPinchAnchor = _simulatedRightAnchor.transform;
                _manipulator?.ConfigureAnchors(_rightPinchAnchor, null, _headsetCamera);
                UpdateEditorSimulation();
                return;
            }
            bool pinch = _hasTrackedAnchor && (TrackedHands.Pinch(Handedness.Right, HandFinger.Index) || ShadowCastXRInput.RawButton("Grab"));
            if (_waitForGrabRelease)
            {
                if (pinch) return;
                _waitForGrabRelease = false;
            }
            if (pinch && !_wasPinching && _manipulator != null)
            {
                if (!_manipulator.BeginPinchGrab() && ShadowCastXRInput.TryPointer(Handedness.Right, out Pose pointing) &&
                    Physics.Raycast(pointing.position, pointing.rotation * Vector3.forward, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                    _manipulator.BeginPinchGrab(hit.collider.gameObject);
            }
            else if (!pinch && _wasPinching) _manipulator?.ReleasePinchGrab();
            _wasPinching = pinch;
            bool undo = TrackedHands.Pinch(Handedness.Left, HandFinger.Middle) || ShadowCastXRInput.RawButton("Undo");
            bool redo = TrackedHands.Pinch(Handedness.Left, HandFinger.Ring) || ShadowCastXRInput.RawButton("Redo");
            if (undo && !undoWasDown) _undoRedo?.Undo();
            if (redo && !redoWasDown) _undoRedo?.Redo();
            undoWasDown = undo; redoWasDown = redo;
        }

        private void OnDisable()
        {
            _manipulator?.ReleasePinchGrab();
            _wasPinching = undoWasDown = redoWasDown = false;
            _waitForGrabRelease = true;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) OnDisable(); }

        private void UpdateEditorSimulation()
        {
            if (_headsetCamera == null || Mouse.current == null) return;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            Vector2 mousePosition = mouse.position.ReadValue();
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _manipulator?.ReleasePinchGrab();
                _wasPinching = false;
            }
            if (!_headsetCamera.pixelRect.Contains(mousePosition)) return;

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
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame) TogglePlay();
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
                Destroy(cube);
            }
        }

        private void HandleBoundaryHit(Vector3 normal) { }
        public void TriggerHapticPulse() { }

        private void TogglePlay()
        {
            if (_campaignFlow != null) _campaignFlow.BakeLevel();
            else _buildModeManager?.ToggleMode();
        }

        private void OnGUI()
        {
            if (!_showDebugGUI || !_enableEditorSimulation || XRSettings.isDeviceActive || (_campaignUI != null && _campaignUI.BlocksBuildInput)) return;

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
                TogglePlay();
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
