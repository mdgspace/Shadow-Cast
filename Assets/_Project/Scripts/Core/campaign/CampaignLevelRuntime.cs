using System.Collections.Generic;
using UnityEngine;
using ShadowCast.Core.BuildMode;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Creates and clears one level's world from its definition without changing scenes.</summary>
    public sealed class CampaignLevelRuntime : MonoBehaviour
    {
        [SerializeField] private BuildModeManager buildModeManager;
        [SerializeField] private CampaignFlowController flowController;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private GameObject goalPrefab;
        [SerializeField] private ArcadeHandControls arcadeControls;
        [SerializeField] private Vector3 characterWallOffset = new Vector3(0f, 0f, 0.02f);

        private readonly List<BuildableObject> levelObjects = new List<BuildableObject>();
        private Transform runtimeRoot;
        private Transform wall;
        private ShadowWallBaker wallBaker;
        private readonly List<Light> levelLights = new List<Light>();
        private readonly List<LightSourceManipulator> lightManipulators = new List<LightSourceManipulator>();
        private GameObject character;
        private LevelDefinition currentLevel;

        public LevelDefinition CurrentLevel => currentLevel;
        public Transform Wall => wall;
        public Vector3 CharacterStartWorldPosition => wall != null && currentLevel != null
            ? wall.TransformPoint(currentLevel.CharacterStartWallLocalPosition + characterWallOffset)
            : Vector3.zero;

        private void Awake()
        {
            if (buildModeManager == null) buildModeManager = GetComponent<BuildModeManager>();
            if (buildModeManager == null) buildModeManager = FindAnyObjectByType<BuildModeManager>();
            if (flowController == null) flowController = GetComponentInParent<CampaignFlowController>();
            if (contentRoot == null) contentRoot = transform;
        }

        public bool LoadLevel(LevelDefinition level, Pose confirmedWallPose)
        {
            if (level == null || buildModeManager == null)
            {
                Debug.LogError("Cannot load campaign level: level definition or BuildModeManager is missing.", this);
                return false;
            }

            ClearLevel();
            currentLevel = level;
            runtimeRoot = new GameObject($"CampaignLevel_{level.Id}").transform;
            runtimeRoot.SetParent(contentRoot, false);

            Pose adjustedPose = new Pose(
                confirmedWallPose.position + confirmedWallPose.rotation * level.WallOffsetFromConfirmedPose,
                confirmedWallPose.rotation);
            CreateWall(level, adjustedPose);
            CreateObjects(level);
            CreateLights(level);
            CreateGoal(level);
            bool primaryLightMovable = level.AllowLightMovement && lightManipulators.Count > 0 && lightManipulators[0].AllowLightMovement;
            buildModeManager.ConfigureForLevel(wall, wallBaker, levelLights.Count > 0 ? levelLights[0] : null, primaryLightMovable);
            level.MechanicHooks?.levelLoaded?.Invoke();
            return true;
        }

        public void StartBuild()
        {
            if (currentLevel != null) currentLevel.MechanicHooks?.buildStarted?.Invoke();
        }

        public void StartBake()
        {
            if (currentLevel != null) currentLevel.MechanicHooks?.bakeStarted?.Invoke();
        }

        public void StartPlay(Vector3 spawnPosition)
        {
            if (currentLevel == null) return;
            if (character != null) Destroy(character);
            character = characterPrefab != null
                ? Instantiate(characterPrefab, spawnPosition, wall.rotation, runtimeRoot)
                : CreatePrimitiveCharacter(spawnPosition);

            WallPlatformerController2D controller = character.GetComponent<WallPlatformerController2D>();
            if (controller == null) controller = character.AddComponent<WallPlatformerController2D>();
            controller.ResetAt(spawnPosition);
            controller.ConfigureFailureBounds(flowController, wall);
            if (arcadeControls != null) arcadeControls.SetCharacter(controller);
            currentLevel.MechanicHooks?.playStarted?.Invoke();
        }

        public void StopPlay()
        {
            if (arcadeControls != null)
            {
                arcadeControls.ReleaseControls();
                arcadeControls.SetCharacter(null);
            }
            if (character != null) Destroy(character);
            character = null;
        }

        public void ClearLevel()
        {
            StopPlay();
            if (buildModeManager != null) buildModeManager.UnregisterLevelObjects(levelObjects);
            levelObjects.Clear();
            currentLevel = null;
            wall = null;
            wallBaker = null;
            levelLights.Clear();
            lightManipulators.Clear();
            if (runtimeRoot != null) Destroy(runtimeRoot.gameObject);
            runtimeRoot = null;
        }

        private void CreateWall(LevelDefinition level, Pose pose)
        {
            GameObject wallObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            wallObject.name = "CampaignProjectionWall";
            wallObject.transform.SetParent(runtimeRoot, false);
            wallObject.transform.SetPositionAndRotation(pose.position, pose.rotation);
            wallObject.transform.localScale = new Vector3(level.WallWidth, level.WallHeight, 1f);
            int wallLayer = LayerMask.NameToLayer("ShadowWall");
            if (wallLayer >= 0) wallObject.layer = wallLayer;

            wall = wallObject.transform;
            wallBaker = wallObject.AddComponent<ShadowWallBaker>();
            wallBaker.wall = wallObject.GetComponent<MeshFilter>();
            wallBaker.showOnGUIBakeButton = false;
            wallBaker.wallLayer = 1 << (wallLayer >= 0 ? wallLayer : wallObject.layer);
            int casterLayer = LayerMask.NameToLayer("ShadowCaster");
            wallBaker.shadowCasterLayer = 1 << (casterLayer >= 0 ? casterLayer : 7);
        }

        private void CreateObjects(LevelDefinition level)
        {
            int casterLayer = LayerMask.NameToLayer("ShadowCaster");
            if (casterLayer < 0) casterLayer = 7;

            foreach (LevelObjectPlacement placement in level.Objects)
            {
                if (placement == null || placement.Prefab == null)
                {
                    Debug.LogWarning($"Level '{level.Id}' contains an object placement without a prefab.", this);
                    continue;
                }

                Vector3 position = wall.TransformPoint(placement.WallLocalPosition);
                Quaternion rotation = wall.rotation * Quaternion.Euler(placement.WallLocalEulerAngles);
                GameObject instance = Instantiate(placement.Prefab, position, rotation, runtimeRoot);
                instance.name = string.IsNullOrWhiteSpace(placement.PlacementId) ? placement.Prefab.name : placement.PlacementId;
                instance.transform.localScale = placement.LocalScale;
                SetLayerRecursively(instance.transform, casterLayer);
                BuildableObject buildable = instance.GetComponent<BuildableObject>();
                if (buildable == null) buildable = instance.AddComponent<BuildableObject>();
                buildModeManager.RegisterLevelObject(buildable, placement.Movable);
                levelObjects.Add(buildable);
            }
        }

        private void CreateLights(LevelDefinition level)
        {
            foreach (LevelLightPlacement placement in level.Lights)
            {
                if (placement == null || placement.Prefab == null)
                {
                    Debug.LogWarning($"Level '{level.Id}' contains a light placement without a prefab.", this);
                    continue;
                }

                Vector3 position = wall.TransformPoint(placement.WallLocalPosition);
                Quaternion rotation = wall.rotation * Quaternion.Euler(placement.WallLocalEulerAngles);
                GameObject instance = Instantiate(placement.Prefab, position, rotation, runtimeRoot);
                instance.name = $"LevelLight_{level.Id}_{levelLights.Count + 1}";
                Light light = instance.GetComponentInChildren<Light>(true);
                if (light == null)
                {
                    Debug.LogWarning($"Light prefab '{placement.Prefab.name}' on level '{level.Id}' contains no Light component.", this);
                    continue;
                }

                LightSourceManipulator manipulator = instance.GetComponent<LightSourceManipulator>();
                if (manipulator == null) manipulator = instance.AddComponent<LightSourceManipulator>();
                manipulator.AllowLightMovement = level.AllowLightMovement && placement.Movable;
                levelLights.Add(light);
                lightManipulators.Add(manipulator);
            }
        }

        private void CreateGoal(LevelDefinition level)
        {
            Vector3 goalPosition = wall.TransformPoint(level.GoalWallLocalPosition);
            GameObject goal = goalPrefab != null ? Instantiate(goalPrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            goal.name = "LevelGoal";
            goal.transform.SetParent(runtimeRoot, false);
            goal.transform.SetPositionAndRotation(goalPosition, wall.rotation);
            goal.transform.localScale = new Vector3(level.GoalSize.x, level.GoalSize.y, 0.04f);
            if (goalPrefab == null)
            {
                Renderer goalRenderer = goal.GetComponent<Renderer>();
                if (goalRenderer != null)
                {
                    Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    material.color = new Color(0.18f, 0.9f, 0.48f);
                    goalRenderer.sharedMaterial = material;
                }
            }
            Collider solidCollider = goal.GetComponent<Collider>();
            if (solidCollider != null) Destroy(solidCollider);
            BoxCollider2D trigger = goal.GetComponent<BoxCollider2D>();
            if (trigger == null) trigger = goal.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            MonoBehaviour condition = null;
            if (level.GoalConditionPrefab != null)
            {
                GameObject conditionObject = Instantiate(level.GoalConditionPrefab, goal.transform);
                MonoBehaviour[] components = conditionObject.GetComponents<MonoBehaviour>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is ICampaignGoalCondition)
                    {
                        condition = components[i];
                        break;
                    }
                }
                if (condition == null)
                    Debug.LogWarning($"Goal condition prefab on level '{level.Id}' has no component implementing ICampaignGoalCondition.", this);
            }
            goal.AddComponent<CampaignGoalTrigger>().Configure(flowController, condition);
        }

        private GameObject CreatePrimitiveCharacter(Vector3 position)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "TemporaryCampaignCharacter";
            player.transform.SetParent(runtimeRoot, true);
            player.transform.SetPositionAndRotation(position, wall.rotation);
            player.transform.localScale = new Vector3(0.18f, 0.24f, 0.06f);
            Renderer playerRenderer = player.GetComponent<Renderer>();
            if (playerRenderer != null)
            {
                Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = new Color(0.95f, 0.38f, 0.72f);
                playerRenderer.sharedMaterial = material;
            }
            Collider collider = player.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Rigidbody2D body = player.AddComponent<Rigidbody2D>();
            body.freezeRotation = true;
            body.gravityScale = 3f;
            CapsuleCollider2D capsule = player.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.85f, 0.95f);
            return player;
        }

        private static void SetLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++) SetLayerRecursively(target.GetChild(i), layer);
        }
    }
}
