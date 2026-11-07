using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ShadowCast.Core.Campaign
{
    [Serializable]
    public sealed class LevelObjectPlacement
    {
        [SerializeField] private string placementId;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector3 wallLocalPosition;
        [SerializeField] private Vector3 wallLocalEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private bool movable = true;

        public string PlacementId => placementId;
        public GameObject Prefab => prefab;
        public Vector3 WallLocalPosition => wallLocalPosition;
        public Vector3 WallLocalEulerAngles => wallLocalEulerAngles;
        public Vector3 LocalScale => localScale;
        public bool Movable => movable;
    }

    [Serializable]
    public sealed class LevelLightPlacement
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private Vector3 wallLocalPosition;
        [SerializeField] private Vector3 wallLocalEulerAngles;
        [SerializeField] private bool movable;

        public GameObject Prefab => prefab;
        public Vector3 WallLocalPosition => wallLocalPosition;
        public Vector3 WallLocalEulerAngles => wallLocalEulerAngles;
        public bool Movable => movable;
    }

    [Serializable]
    public sealed class LevelMechanicHooks
    {
        public UnityEvent levelLoaded;
        public UnityEvent buildStarted;
        public UnityEvent bakeStarted;
        public UnityEvent playStarted;
        public UnityEvent levelCompleted;
        public UnityEvent levelFailed;
    }

    [CreateAssetMenu(menuName = "Shadow Cast/Campaign/Level", fileName = "Level")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite artwork;
        [SerializeField, Min(0.5f)] private float wallWidth = 3f;
        [SerializeField, Min(0.5f)] private float wallHeight = 2f;
        [SerializeField] private Vector3 wallOffsetFromConfirmedPose;
        [SerializeField] private List<LevelObjectPlacement> objects = new List<LevelObjectPlacement>();
        [SerializeField] private List<LevelLightPlacement> lights = new List<LevelLightPlacement>();
        [SerializeField] private bool allowLightMovement;
        [SerializeField] private Vector3 characterStartWallLocalPosition = new Vector3(-1.2f, -0.8f, -0.02f);
        [SerializeField] private Vector3 goalWallLocalPosition = new Vector3(1.2f, -0.7f, -0.02f);
        [SerializeField] private Vector2 goalSize = new Vector2(0.25f, 0.4f);
        [SerializeField] private GameObject goalConditionPrefab;
        [SerializeField] private LevelMechanicHooks mechanicHooks = new LevelMechanicHooks();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Artwork => artwork;
        public float WallWidth => wallWidth;
        public float WallHeight => wallHeight;
        public Vector3 WallOffsetFromConfirmedPose => wallOffsetFromConfirmedPose;
        public IReadOnlyList<LevelObjectPlacement> Objects => objects;
        public IReadOnlyList<LevelLightPlacement> Lights => lights;
        public bool AllowLightMovement => allowLightMovement;
        public Vector3 CharacterStartWallLocalPosition => characterStartWallLocalPosition;
        public Vector3 GoalWallLocalPosition => goalWallLocalPosition;
        public Vector2 GoalSize => goalSize;
        public GameObject GoalConditionPrefab => goalConditionPrefab;
        public LevelMechanicHooks MechanicHooks => mechanicHooks;

        private void OnValidate()
        {
            id = string.IsNullOrWhiteSpace(id) ? name : id.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
            wallWidth = Mathf.Max(0.5f, wallWidth);
            wallHeight = Mathf.Max(0.5f, wallHeight);
            goalSize = new Vector2(Mathf.Max(0.05f, goalSize.x), Mathf.Max(0.05f, goalSize.y));
        }
    }
}
