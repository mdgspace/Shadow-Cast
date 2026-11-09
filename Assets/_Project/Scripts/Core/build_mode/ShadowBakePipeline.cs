using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ShadowCast.Core.Campaign;

namespace ShadowCast.Core.BuildMode
{
    public enum BakeState
    {
        Idle,
        Baking,
        BakedPlayActive
    }

    /// <summary>
    /// Executes the Shadow Baking Pipeline by integrating directly with ShadowWallBaker (new_shadow_approach).
    /// 1. Freezes object transforms and interactions.
    /// 2. Moves build objects to the shadow caster layer.
    /// 3. Triggers ShadowWallBaker.Bake() to generate crisp/soft 2D PolygonColliders and quicksand effectors.
    /// 4. Determines the 2D character spawn position on the lowest platform.
    /// 5. Transitions to Play Mode.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShadowBakePipeline : MonoBehaviour
    {
        [Header("Baker Engine Reference (Auto-resolved if null)")]
        [Tooltip("The ShadowWallBaker component from new_shadow_approach. Auto-detected in scene if left empty.")]
        [SerializeField] private ShadowWallBaker _shadowWallBaker;

        [Header("Target Wall Plane")]
        [Tooltip("Transform representing the primary physical wall plane where shadows are cast.")]
        [SerializeField] private Transform _targetWallPlane;

        [Header("Light Source Reference")]
        [SerializeField] private Light _lightSource;

        [Header("Character Spawn Settings")]
        [Tooltip("Initial spawn anchor offset relative to the lowest generated platform.")]
        [SerializeField] private Vector3 _characterSpawnOffset = new Vector3(0f, 0.25f, -0.05f);

        // State tracking
        private BakeState _currentState = BakeState.Idle;
        private readonly List<BuildableObject> _activeBakedObjects = new List<BuildableObject>();
        private readonly List<MovingShadowPlatform> _activeMovingShadows = new List<MovingShadowPlatform>();
        private Vector3 _calculatedCharacterSpawnPos = Vector3.zero;
        private Vector3? _requestedCharacterSpawnPos;

        // Events
        public event Action<BakeState> OnBakeStateChanged;
        public event Action<float> OnBakeProgress;
        public event Action<Vector3> OnBakeCompleted; // (characterSpawnPoint)
        public event Action OnUnbaked;
        public event Action<string> OnBakeFailed;

        public BakeState CurrentState => _currentState;
        public Vector3 CharacterSpawnPosition => _calculatedCharacterSpawnPos;
        public ShadowWallBaker WallBaker
        {
            get => _shadowWallBaker;
            set => _shadowWallBaker = value;
        }

        public Transform TargetWallPlane
        {
            get => _targetWallPlane;
            set
            {
                _targetWallPlane = value;
                if (_shadowWallBaker != null && value != null)
                {
                    MeshFilter mf = value.GetComponent<MeshFilter>();
                    if (mf != null) _shadowWallBaker.wall = mf;
                }
            }
        }

        public Light LightSource
        {
            get => _lightSource;
            set => _lightSource = value;
        }

        private void Awake()
        {
            EnsureDependencies();
        }

        private void EnsureDependencies()
        {
            if (_shadowWallBaker == null)
            {
                _shadowWallBaker = FindAnyObjectByType<ShadowWallBaker>();
            }

            if (_lightSource == null)
            {
                _lightSource = FindAnyObjectByType<Light>();
            }

            if (_targetWallPlane == null)
            {
                if (_shadowWallBaker != null && _shadowWallBaker.wall != null)
                {
                    _targetWallPlane = _shadowWallBaker.wall.transform;
                }
                else
                {
                    GameObject wallObj = GameObject.FindWithTag("Wall") ?? GameObject.Find("Wall") ?? GameObject.Find("wall");
                    if (wallObj != null)
                    {
                        _targetWallPlane = wallObj.transform;
                    }
                }
            }

            // Sync wall reference to ShadowWallBaker
            if (_shadowWallBaker != null && _targetWallPlane != null && _shadowWallBaker.wall == null)
            {
                _shadowWallBaker.wall = _targetWallPlane.GetComponent<MeshFilter>();
            }
        }

        /// <summary>
        /// Executes the baking sequence using ShadowWallBaker.
        /// </summary>
        public void TriggerBake(List<BuildableObject> buildObjects, Vector3? characterSpawnPoint = null)
        {
            if (_currentState == BakeState.Baking) return;
            _requestedCharacterSpawnPos = characterSpawnPoint;
            StartCoroutine(ExecuteBakeRoutine(buildObjects));
        }

        /// <summary>
        /// Restores objects to unbaked Build Mode state and clears baked platforms from ShadowWallBaker.
        /// </summary>
        public void Unbake()
        {
            if (_shadowWallBaker != null)
            {
                _shadowWallBaker.ClearBakedPlatforms();
            }

            for (int i = 0; i < _activeMovingShadows.Count; i++)
                if (_activeMovingShadows[i] != null) _activeMovingShadows[i].RestoreBuildState();
            _activeMovingShadows.Clear();

            // Restore interactions and layers on all active build objects
            for (int i = 0; i < _activeBakedObjects.Count; i++)
            {
                if (_activeBakedObjects[i] != null)
                {
                    _activeBakedObjects[i].SetInteractionsEnabled(true);
                    _activeBakedObjects[i].RestoreOriginalLayer();
                }
            }
            _activeBakedObjects.Clear();

            _currentState = BakeState.Idle;
            OnBakeStateChanged?.Invoke(_currentState);
            OnUnbaked?.Invoke();
        }

        private IEnumerator ExecuteBakeRoutine(List<BuildableObject> buildObjects)
        {
            _currentState = BakeState.Baking;
            OnBakeStateChanged?.Invoke(_currentState);
            OnBakeProgress?.Invoke(0.1f);

            EnsureDependencies();
            _activeBakedObjects.Clear();
            _activeMovingShadows.Clear();

            MovingShadowPlatform[] movingShadows = FindObjectsByType<MovingShadowPlatform>(FindObjectsInactive.Exclude);
            for (int i = 0; i < movingShadows.Length; i++)
            {
                MovingShadowPlatform moving = movingShadows[i];
                if (moving == null || !moving.gameObject.activeInHierarchy) continue;
                moving.PrepareForBake(_targetWallPlane);
                _activeMovingShadows.Add(moving);
            }

            // 1. Determine shadow caster layer from ShadowWallBaker
            int casterLayer = 7; // Default shadow caster layer
            if (_shadowWallBaker != null)
            {
                casterLayer = LayerMaskToLayerIndex(_shadowWallBaker.shadowCasterLayer, 7);
            }
            else
            {
                int namedLayer = LayerMask.NameToLayer("ShadowCasters");
                if (namedLayer != -1) casterLayer = namedLayer;
            }

            // 2. Freeze transforms & reassign layers so snapshot camera captures them
            for (int i = 0; i < buildObjects.Count; i++)
            {
                BuildableObject bo = buildObjects[i];
                if (bo == null || !bo.gameObject.activeInHierarchy) continue;

                bo.SetInteractionsEnabled(false);
                bo.SetLayerRecursively(casterLayer);
                _activeBakedObjects.Add(bo);
            }

            OnBakeProgress?.Invoke(0.35f);
            yield return null; // Wait 1 frame for physics & transforms to settle

            // 3. Execute bake with ShadowWallBaker
            bool bakeSucceeded = false;
            if (_shadowWallBaker != null)
            {
                if (_targetWallPlane != null && _shadowWallBaker.wall == null)
                {
                    _shadowWallBaker.wall = _targetWallPlane.GetComponent<MeshFilter>();
                }

                bakeSucceeded = _shadowWallBaker.Bake();
            }
            else
            {
                Debug.LogWarning("[ShadowBakePipeline] No ShadowWallBaker found in scene. Adding one dynamically to target wall.");
                if (_targetWallPlane != null)
                {
                    _shadowWallBaker = _targetWallPlane.gameObject.AddComponent<ShadowWallBaker>();
                    _shadowWallBaker.wall = _targetWallPlane.GetComponent<MeshFilter>();
                    bakeSucceeded = _shadowWallBaker.Bake();
                }
            }

            bool movingShadowSucceeded = false;
            for (int i = 0; i < _activeMovingShadows.Count; i++)
                if (_activeMovingShadows[i] != null)
                    movingShadowSucceeded |= _activeMovingShadows[i].CreateBakedShadow(_targetWallPlane);

            CreateTrapProxies();

            if (!bakeSucceeded && !movingShadowSucceeded)
            {
                _requestedCharacterSpawnPos = null;
                Unbake();
                OnBakeFailed?.Invoke("No playable shadow platforms were generated. Adjust the objects or light, then bake again.");
                yield break;
            }

            OnBakeProgress?.Invoke(0.85f);
            yield return null;

            // 4. Calculate 2D Character Spawn Position
            _calculatedCharacterSpawnPos = _requestedCharacterSpawnPos ?? CalculateCharacterSpawnPoint();
            _requestedCharacterSpawnPos = null;

            OnBakeProgress?.Invoke(1.0f);
            _currentState = BakeState.BakedPlayActive;
            OnBakeStateChanged?.Invoke(_currentState);
            OnBakeCompleted?.Invoke(_calculatedCharacterSpawnPos);
        }

        private void CreateTrapProxies()
        {
            if (_targetWallPlane == null) return;
            Transform root = _targetWallPlane.Find("BakedShadowPlatforms") ?? _targetWallPlane;
            for (int i = 0; i < _activeBakedObjects.Count; i++)
            {
                BuildableObject buildable = _activeBakedObjects[i];
                if (buildable == null || !buildable.IsTrap) continue;

                GameObject trap = new GameObject($"TrapShadow_{buildable.name}");
                trap.transform.SetParent(root, false);
                Vector3 localPosition = _targetWallPlane.InverseTransformPoint(buildable.transform.position);
                localPosition.z = -0.006f;
                trap.transform.localPosition = localPosition;

                Vector3 extents = buildable.GetExtents();
                Vector3 wallScale = _targetWallPlane.lossyScale;
                Vector2 size = new Vector2(
                    Mathf.Max(0.05f, extents.x * 2f / Mathf.Max(0.0001f, Mathf.Abs(wallScale.x))),
                    Mathf.Max(0.05f, extents.y * 2f / Mathf.Max(0.0001f, Mathf.Abs(wallScale.y))));
                BoxCollider2D trigger = trap.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.size = size;
                trap.AddComponent<ShadowTrapHazard>();
            }
        }

        private Vector3 CalculateCharacterSpawnPoint()
        {
            // Look for generated platforms under target wall
            Transform wallTransform = _targetWallPlane;
            if (_shadowWallBaker != null && _shadowWallBaker.wall != null)
            {
                wallTransform = _shadowWallBaker.wall.transform;
            }

            if (wallTransform != null)
            {
                Transform platformsRoot = wallTransform.Find("BakedShadowPlatforms");
                if (platformsRoot != null && platformsRoot.childCount > 0)
                {
                    float lowestY = float.MaxValue;
                    Vector3 bestPoint = Vector3.zero;
                    bool found = false;

                    // Prefer crisp solid platforms first
                    for (int i = 0; i < platformsRoot.childCount; i++)
                    {
                        Transform child = platformsRoot.GetChild(i);
                        if (child.name.Contains("Crisp"))
                        {
                            Vector3 pos = child.position;
                            if (pos.y < lowestY)
                            {
                                lowestY = pos.y;
                                bestPoint = pos;
                                found = true;
                            }
                        }
                    }

                    // Fallback to any platform if no crisp platform exists
                    if (!found)
                    {
                        for (int i = 0; i < platformsRoot.childCount; i++)
                        {
                            Transform child = platformsRoot.GetChild(i);
                            Vector3 pos = child.position;
                            if (pos.y < lowestY)
                            {
                                lowestY = pos.y;
                                bestPoint = pos;
                                found = true;
                            }
                        }
                    }

                    if (found)
                    {
                        return bestPoint + _characterSpawnOffset;
                    }
                }

                return wallTransform.position + Vector3.up * 0.5f + _characterSpawnOffset;
            }

            return Vector3.up * 0.5f;
        }

        private int LayerMaskToLayerIndex(LayerMask mask, int defaultLayer)
        {
            int val = mask.value;
            if (val <= 0) return defaultLayer;
            for (int i = 0; i < 32; i++)
            {
                if ((val & (1 << i)) != 0) return i;
            }
            return defaultLayer;
        }
    }
}
