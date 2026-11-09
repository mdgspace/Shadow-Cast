using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    public enum GameMode
    {
        BuildMode,
        PlayMode
    }

    /// <summary>
    /// Master coordinator for the AR Shadow Platformer Build Mode system.
    /// Simplified Inspector: only displays essential scene integration hooks.
    /// All internal subcomponents (BoundingVolume, SnapEngine, Manipulator, BakePipeline, UndoRedo, VR Input)
    /// are auto-resolved and hidden from the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoundingVolumeManager))]
    [RequireComponent(typeof(SurfaceSnapEngine))]
    [RequireComponent(typeof(ObjectManipulator))]
    [RequireComponent(typeof(ShadowBakePipeline))]
    [RequireComponent(typeof(BuildUndoRedoManager))]
    [RequireComponent(typeof(VRBuildInputBridge))]
    public class BuildModeManager : MonoBehaviour
    {
        #region Manual Integration Settings (Visible in Inspector)

        [Header("Scene Integration (Configure for your scene)")]
        [Tooltip("Optional reference to ShadowWallBaker (new_shadow_approach). Auto-detected in scene if left empty.")]
        [SerializeField] private ShadowWallBaker _shadowWallBaker;

        [Tooltip("The physical or virtual wall transform where shadows are projected and platform colliders are generated.")]
        [SerializeField] private Transform _targetWallPlane;

        [Tooltip("The light source casting the platform shadows. If null, automatically uses the first light found in the scene.")]
        [SerializeField] private Light _lightSource;

        [Tooltip("Developer toggle: Allow the player to grab and move the light source in this level?")]
        [SerializeField] private bool _allowLightMovement = true;

        [Header("Buildable Prefabs (Palette)")]
        [Tooltip("List of 3D primitive / platform prefabs that can be spawned in this level.")]
        [SerializeField] private List<GameObject> _buildablePrefabs = new List<GameObject>();

        [Header("Runtime Status")]
        [Tooltip("Current operational mode of the system.")]
        [SerializeField] private GameMode _currentMode = GameMode.BuildMode;

        #endregion

        #region Internal Auto-Resolved Modules (Hidden from Inspector)

        private BoundingVolumeManager _boundingVolume;
        private SurfaceSnapEngine _snapEngine;
        private ObjectManipulator _manipulator;
        private ShadowBakePipeline _bakePipeline;
        private BuildUndoRedoManager _undoRedoManager;
        private LightSourceManipulator _lightManipulator;
        private VRBuildInputBridge _inputBridge;
        private Transform _objectsContainer;

        #endregion

        private readonly List<BuildableObject> _activeObjects = new List<BuildableObject>();
        private BuildableObject _selectedObject;

        // Events for UI and external systems
        public event Action<GameMode> OnModeChanged;
        public event Action<BuildableObject> OnObjectSpawned;
        public event Action<BuildableObject> OnObjectDeleted;
        public event Action<BuildableObject> OnObjectSelected;
        public event Action<bool> OnGridSnapToggled;
        public event Action<Vector3> OnBakeCompleted;

        public GameMode CurrentMode => _currentMode;
        public bool IsBuildMode => _currentMode == GameMode.BuildMode;
        public bool IsPlayMode => _currentMode == GameMode.PlayMode;

        public ShadowWallBaker WallBaker
        {
            get => _shadowWallBaker;
            set
            {
                _shadowWallBaker = value;
                if (_bakePipeline != null) _bakePipeline.WallBaker = value;
            }
        }

        public Transform TargetWallPlane
        {
            get => _targetWallPlane;
            set
            {
                _targetWallPlane = value;
                if (_bakePipeline != null) _bakePipeline.TargetWallPlane = value;
            }
        }

        public Light LightSource
        {
            get => _lightSource;
            set
            {
                _lightSource = value;
                if (_bakePipeline != null) _bakePipeline.LightSource = value;
            }
        }

        public bool AllowLightMovement
        {
            get => _allowLightMovement;
            set
            {
                _allowLightMovement = value;
                if (_lightManipulator != null) _lightManipulator.AllowLightMovement = value;
            }
        }

        public IReadOnlyList<GameObject> BuildablePrefabs => _buildablePrefabs;
        public IReadOnlyList<BuildableObject> ActiveObjects => _activeObjects;
        public BuildableObject SelectedObject => _selectedObject;

        public BoundingVolumeManager BoundingVolume => _boundingVolume;
        public SurfaceSnapEngine SnapEngine => _snapEngine;
        public ObjectManipulator Manipulator => _manipulator;
        public ShadowBakePipeline BakePipeline => _bakePipeline;
        public BuildUndoRedoManager UndoRedoManager => _undoRedoManager;
        public LightSourceManipulator LightManipulator => _lightManipulator;
        public VRBuildInputBridge InputBridge => _inputBridge;

        private void Awake()
        {
            InitializeInternalModules();
            ApplyIntegrationSettings();
        }

        private void Start()
        {
            if (_bakePipeline != null)
            {
                _bakePipeline.OnBakeCompleted += HandleBakeCompleted;
            }

            if (_manipulator != null)
            {
                _manipulator.OnObjectGrabbed += HandleObjectGrabbed;
                _manipulator.OnObjectDeletedByShake += HandleObjectDeletedByShake;
            }

            // Register existing objects in container if any
            if (_objectsContainer != null)
            {
                BuildableObject[] existing = _objectsContainer.GetComponentsInChildren<BuildableObject>(true);
                for (int i = 0; i < existing.Length; i++)
                {
                    if (!_activeObjects.Contains(existing[i]))
                    {
                        _activeObjects.Add(existing[i]);
                        GroundObjectIfNeeded(existing[i]);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_bakePipeline != null)
            {
                _bakePipeline.OnBakeCompleted -= HandleBakeCompleted;
            }

            if (_manipulator != null)
            {
                _manipulator.OnObjectGrabbed -= HandleObjectGrabbed;
                _manipulator.OnObjectDeletedByShake -= HandleObjectDeletedByShake;
            }
        }

        private void InitializeInternalModules()
        {
            _boundingVolume = GetComponent<BoundingVolumeManager>() ?? gameObject.AddComponent<BoundingVolumeManager>();
            _snapEngine = GetComponent<SurfaceSnapEngine>() ?? gameObject.AddComponent<SurfaceSnapEngine>();
            _manipulator = GetComponent<ObjectManipulator>() ?? gameObject.AddComponent<ObjectManipulator>();
            _bakePipeline = GetComponent<ShadowBakePipeline>() ?? gameObject.AddComponent<ShadowBakePipeline>();
            _undoRedoManager = GetComponent<BuildUndoRedoManager>() ?? gameObject.AddComponent<BuildUndoRedoManager>();
            _inputBridge = GetComponent<VRBuildInputBridge>() ?? gameObject.AddComponent<VRBuildInputBridge>();

            if (_objectsContainer == null)
            {
                GameObject container = new GameObject("[BuildObjects_Container]");
                container.transform.SetParent(transform);
                _objectsContainer = container.transform;
            }
        }

        private void ApplyIntegrationSettings()
        {
            // Auto-locate light if not set
            if (_lightSource == null)
            {
                _lightSource = FindAnyObjectByType<Light>();
            }

            if (_lightSource != null)
            {
                _lightManipulator = _lightSource.GetComponent<LightSourceManipulator>();
                if (_lightManipulator == null)
                {
                    _lightManipulator = _lightSource.gameObject.AddComponent<LightSourceManipulator>();
                }
                _lightManipulator.AllowLightMovement = _allowLightMovement;
            }

            // Auto-locate wall if not set
            if (_targetWallPlane == null)
            {
                GameObject wallObj = GameObject.FindWithTag("Wall") ?? GameObject.Find("Wall");
                if (wallObj != null)
                {
                    _targetWallPlane = wallObj.transform;
                }
            }

            if (_shadowWallBaker == null)
            {
                _shadowWallBaker = FindAnyObjectByType<ShadowWallBaker>();
            }

            if (_bakePipeline != null)
            {
                if (_shadowWallBaker != null) _bakePipeline.WallBaker = _shadowWallBaker;
                _bakePipeline.TargetWallPlane = _targetWallPlane;
                _bakePipeline.LightSource = _lightSource;
            }
        }

        #region Spawning & Lifecycle API

        /// <summary>
        /// Spawns a prefab from the registered palette by index.
        /// </summary>
        public BuildableObject SpawnPrefabIndex(int index, Vector3 spawnPosition = default, Quaternion spawnRotation = default)
        {
            if (index < 0 || index >= _buildablePrefabs.Count) return null;
            return SpawnObject(_buildablePrefabs[index], spawnPosition, spawnRotation);
        }

        /// <summary>
        /// Spawns a new buildable object into the active build space, adds it to the undo stack,
        /// and enforces bounding volume clamping.
        /// </summary>
        public BuildableObject SpawnObject(GameObject prefab, Vector3 spawnPosition = default, Quaternion spawnRotation = default)
        {
            if (prefab == null || !IsBuildMode) return null;

            if (spawnPosition == default)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    spawnPosition = cam.transform.position + cam.transform.forward * 1.2f;
                }
                else
                {
                    spawnPosition = _boundingVolume != null ? _boundingVolume.Center : Vector3.forward * 1.5f;
                }
            }

            if (spawnRotation == default)
            {
                spawnRotation = Quaternion.identity;
            }

            GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation, _objectsContainer);
            BuildableObject buildable = instance.GetComponent<BuildableObject>() ?? instance.AddComponent<BuildableObject>();

            // Clamp initial position to bounding envelope
            if (_boundingVolume != null)
            {
                Vector3 clamped = _boundingVolume.ClampPosition(instance.transform.position, buildable.GetExtents());
                instance.transform.position = clamped;
            }
            GroundObjectIfNeeded(buildable);

            _activeObjects.Add(buildable);
            SelectObject(buildable);

            // Record SpawnCommand in Undo history
            if (_undoRedoManager != null)
            {
                SpawnCommand cmd = new SpawnCommand(
                    instance,
                    onRestore: (go) =>
                    {
                        BuildableObject b = go.GetComponent<BuildableObject>();
                        if (b != null && !_activeObjects.Contains(b)) _activeObjects.Add(b);
                    },
                    onDestroy: (go) =>
                    {
                        BuildableObject b = go.GetComponent<BuildableObject>();
                        if (b != null) _activeObjects.Remove(b);
                    }
                );
                _undoRedoManager.RecordCommand(cmd, executeNow: false);
            }

            OnObjectSpawned?.Invoke(buildable);
            return buildable;
        }

        public void DeleteSelected()
        {
            if (_selectedObject != null)
            {
                DeleteObject(_selectedObject.gameObject);
            }
        }

        public void DeleteObject(GameObject target)
        {
            if (target == null || !IsBuildMode) return;

            BuildableObject buildable = target.GetComponentInParent<BuildableObject>();
            if (buildable != null)
            {
                _activeObjects.Remove(buildable);
                if (_selectedObject == buildable)
                {
                    _selectedObject = null;
                }
            }

            if (_undoRedoManager != null)
            {
                DeleteCommand cmd = new DeleteCommand(
                    target,
                    onDeleted: (go) =>
                    {
                        BuildableObject b = go.GetComponent<BuildableObject>();
                        if (b != null) _activeObjects.Remove(b);
                    },
                    onRestored: (go) =>
                    {
                        BuildableObject b = go.GetComponent<BuildableObject>();
                        if (b != null && !_activeObjects.Contains(b)) _activeObjects.Add(b);
                    }
                );
                _undoRedoManager.RecordCommand(cmd, executeNow: true);
            }
            else
            {
                target.SetActive(false);
            }

            if (buildable != null)
            {
                OnObjectDeleted?.Invoke(buildable);
            }
        }

        public void SelectObject(BuildableObject buildable)
        {
            _selectedObject = buildable;
            OnObjectSelected?.Invoke(_selectedObject);
        }

        /// <summary>Registers a level-authored object with the reusable build-mode system.</summary>
        public void RegisterLevelObject(BuildableObject buildable, bool movable)
        {
            if (buildable == null) return;
            buildable.SetMovable(movable);
            GroundObjectIfNeeded(buildable);
            if (!_activeObjects.Contains(buildable)) _activeObjects.Add(buildable);
        }

        private void GroundObjectIfNeeded(BuildableObject buildable)
        {
            if (buildable == null || !buildable.GravityEnabled) return;
            Vector3 extents = buildable.GetExtents();
            Vector3 position = buildable.transform.position;
            if (_snapEngine == null || !_snapEngine.TryConstrainToGround(position, extents, buildable.transform, out Vector3 grounded))
                grounded = _boundingVolume != null ? _boundingVolume.ConstrainToGround(position, extents) : position;
            buildable.transform.position = _boundingVolume != null
                ? _boundingVolume.ClampPosition(grounded, extents)
                : grounded;
        }

        /// <summary>Clears runtime object registrations between independently authored levels.</summary>
        public void ResetForLevel()
        {
            if (_bakePipeline != null && _bakePipeline.CurrentState != BakeState.Idle)
                _bakePipeline.Unbake();
            _activeObjects.Clear();
            _selectedObject = null;
            _currentMode = GameMode.BuildMode;
            OnModeChanged?.Invoke(_currentMode);
        }

        public void UnregisterLevelObjects(IReadOnlyCollection<BuildableObject> objects)
        {
            if (_bakePipeline != null && _bakePipeline.CurrentState != BakeState.Idle)
                _bakePipeline.Unbake();
            if (objects != null)
            {
                foreach (BuildableObject buildable in objects)
                {
                    _activeObjects.Remove(buildable);
                    if (_selectedObject == buildable) _selectedObject = null;
                }
            }
            _currentMode = GameMode.BuildMode;
            OnModeChanged?.Invoke(_currentMode);
        }

        /// <summary>Applies the selected level's wall and light references at runtime.</summary>
        public void ConfigureForLevel(Transform wall, ShadowWallBaker baker, Light light, bool allowLightMovement)
        {
            TargetWallPlane = wall;
            WallBaker = baker;
            LightSource = light;
            AllowLightMovement = allowLightMovement;
            if (_boundingVolume != null && wall != null)
                _boundingVolume.ConfigureForWall(wall, wall.localScale.x, wall.localScale.y);
            if (light != null)
            {
                _lightManipulator = light.GetComponentInParent<LightSourceManipulator>();
                if (_lightManipulator == null) _lightManipulator = light.gameObject.AddComponent<LightSourceManipulator>();
                _lightManipulator.AllowLightMovement = allowLightMovement;
            }
        }

        public void Deselect()
        {
            _selectedObject = null;
            OnObjectSelected?.Invoke(null);
        }

        #endregion

        #region Undo / Redo & Grid Snap

        public void Undo()
        {
            if (!IsBuildMode) return;
            _undoRedoManager?.Undo();
        }

        public void Redo()
        {
            if (!IsBuildMode) return;
            _undoRedoManager?.Redo();
        }

        public void ToggleGridSnap()
        {
            if (_snapEngine != null)
            {
                _snapEngine.EnableGridSnap = !_snapEngine.EnableGridSnap;
                OnGridSnapToggled?.Invoke(_snapEngine.EnableGridSnap);
            }
        }

        public void SetGridSnap(bool enabled)
        {
            if (_snapEngine != null)
            {
                _snapEngine.EnableGridSnap = enabled;
                OnGridSnapToggled?.Invoke(enabled);
            }
        }

        public void SetGridTranslationIncrement(GridTranslationIncrement increment)
        {
            if (_snapEngine != null)
            {
                _snapEngine.TranslationIncrement = increment;
            }
        }

        public void SetGridRotationIncrement(GridRotationIncrement increment)
        {
            if (_snapEngine != null)
            {
                _snapEngine.RotationIncrement = increment;
            }
        }

        #endregion

        #region Mode Switching & Baking

        public void TriggerBake(Vector3? characterSpawnPoint = null)
        {
            if (!IsBuildMode || _bakePipeline == null) return;
            Deselect();
            _bakePipeline.TriggerBake(_activeObjects, characterSpawnPoint);
        }

        public void UnbakeAndReturnToBuild()
        {
            if (_bakePipeline != null)
            {
                _bakePipeline.Unbake();
            }

            _currentMode = GameMode.BuildMode;
            OnModeChanged?.Invoke(_currentMode);
        }

        public void ToggleMode()
        {
            if (IsBuildMode)
            {
                TriggerBake();
            }
            else
            {
                UnbakeAndReturnToBuild();
            }
        }

        private void HandleBakeCompleted(Vector3 characterSpawnPoint)
        {
            _currentMode = GameMode.PlayMode;
            OnModeChanged?.Invoke(_currentMode);
            OnBakeCompleted?.Invoke(characterSpawnPoint);
        }

        private void HandleObjectGrabbed(GameObject grabbed)
        {
            BuildableObject bo = grabbed.GetComponentInParent<BuildableObject>();
            if (bo != null)
            {
                SelectObject(bo);
            }
        }

        private void HandleObjectDeletedByShake(GameObject deleted)
        {
            BuildableObject bo = deleted.GetComponentInParent<BuildableObject>();
            if (bo != null)
            {
                _activeObjects.Remove(bo);
                if (_selectedObject == bo)
                {
                    _selectedObject = null;
                }
                OnObjectDeleted?.Invoke(bo);
            }
        }

        #endregion
    }
}
