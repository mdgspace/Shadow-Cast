using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using Oculus.Interaction.Input;
using HandFinger = Oculus.Interaction.Input.HandFinger;
using Hand = Oculus.Interaction.Input.Hand;
using ShadowCast.Core.BuildMode;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Starts the complete sample campaign in any scene containing a MainCamera.</summary>
    public sealed class ShadowCastExperienceBootstrap : MonoBehaviour
    {
        [Header("Campaign (leave empty to use the generated sample)")]
        [SerializeField] private List<ChapterDefinition> campaignChapters = new List<ChapterDefinition>();

        [Header("Optional replaceable model prefabs (unit sized)")]
        [SerializeField] private GameObject blockPrefab;
        [SerializeField] private GameObject rampPrefab;
        [SerializeField] private GameObject orbPrefab;
        [SerializeField] private GameObject spotlightPrefab;
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private GameObject goalPrefab;

        public GameObject BlockPrefab { get; private set; }
        public GameObject RampPrefab { get; private set; }
        public GameObject OrbPrefab { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartExperience()
        {
            if (FindAnyObjectByType<ShadowCastExperienceBootstrap>() != null) return;
            GameObject root = new GameObject("Shadow Cast Experience");
            DontDestroyOnLoad(root);
            root.AddComponent<ShadowCastExperienceBootstrap>();
        }

        private void Awake()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0f, 1.6f, 0f);
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            ShadowCastXRInput.EnsureCameraTracking(camera);
            ShadowCastXRInput.ConfigureStationaryPlayer(camera);
            MixedRealityPassthroughBootstrap.Ensure(gameObject, camera);
            EnsureEventSystem();
            BuildModeManager build = FindAnyObjectByType<BuildModeManager>();
            if (build == null) build = gameObject.AddComponent<BuildModeManager>();
            XRHandAnchorDriver anchors = gameObject.AddComponent<XRHandAnchorDriver>();
            Transform rightAnchor = new GameObject("ShadowCast_RightHandPinchAnchor").transform;
            Transform leftAnchor = new GameObject("ShadowCast_LeftHandPointAnchor").transform;
            rightAnchor.SetParent(transform, false); leftAnchor.SetParent(transform, false);
            anchors.Initialize(rightAnchor, leftAnchor, build, camera);
            CampaignLevelRuntime runtime = gameObject.AddComponent<CampaignLevelRuntime>();
            CampaignFlowController flow = gameObject.AddComponent<CampaignFlowController>();
            // CampaignLevelRuntime.Awake runs as soon as the component is added, before the
            // flow component exists. Wire it explicitly so goals and failure bounds can
            // report back to the campaign instead of silently receiving a null controller.
            SetField(runtime, "flowController", flow);
            List<ChapterDefinition> activeChapters = new List<ChapterDefinition>();
            for (int i = 0; i < campaignChapters.Count; i++)
                if (campaignChapters[i] != null) activeChapters.Add(campaignChapters[i]);
            if (activeChapters.Count == 0) activeChapters.Add(CreateSampleCampaign());
            SetField(runtime, "characterPrefab", characterPrefab);
            SetField(runtime, "goalPrefab", goalPrefab);
            SetField(flow, "chapters", activeChapters);
            SetField(flow, "levelRuntime", runtime);
            SetField(flow, "buildModeManager", build);
            WallPlacementController wallPlacement = gameObject.AddComponent<WallPlacementController>();
            wallPlacement.Initialize(camera);

            ShadowCastHeadsetUI ui = gameObject.AddComponent<ShadowCastHeadsetUI>();
            CampaignGameplayHUD gameplayHud = gameObject.AddComponent<CampaignGameplayHUD>();
            gameplayHud.Initialize(flow, camera);
            ui.Initialize(flow, build, camera, this, gameplayHud, wallPlacement);
            Debug.Log($"[Shadow Cast] Campaign UI ready in '{camera.gameObject.scene.name}' using '{camera.name}'.", this);
        }

        private ChapterDefinition CreateSampleCampaign()
        {
            GameObject cube = blockPrefab != null ? blockPrefab : PrimitiveTemplate("Block", PrimitiveType.Cube, new Color(0.16f, 0.68f, 0.85f));
            GameObject wedge = rampPrefab != null ? rampPrefab : PrimitiveTemplate("Wedge", PrimitiveType.Cube, new Color(0.98f, 0.56f, 0.22f));
            GameObject sphere = orbPrefab != null ? orbPrefab : PrimitiveTemplate("Orb", PrimitiveType.Sphere, new Color(0.7f, 0.39f, 0.9f));
            GameObject lamp = spotlightPrefab != null ? spotlightPrefab : CreateSpotlightTemplate();
            BlockPrefab = cube; RampPrefab = wedge; OrbPrefab = sphere;

            LevelDefinition level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.name = "First Light";
            SetField(level, "id", "first-light"); SetField(level, "displayName", "First Light");
            SetField(level, "wallWidth", 3.2f); SetField(level, "wallHeight", 2.1f);
            SetField(level, "characterStartWallLocalPosition", new Vector3(-0.92f, -0.40f, -0.035f));
            SetField(level, "goalWallLocalPosition", new Vector3(1.30f, -0.50f, -0.035f));
            SetField(level, "goalSize", new Vector2(0.24f, 0.36f));
            SetField(level, "baseScore", 1000);
            SetField(level, "minimumScore", 200);
            SetField(level, "hintPenalty", 200);
            SetField(level, "hints", new List<string>
            {
                "Start by making one continuous shadow path from the blob toward the middle of the wall.",
                "Rotate the wedge so its shadow becomes a climbable ramp instead of a vertical obstacle.",
                "The final platform only needs to bring the blob close enough to touch the green exit."
            });
            var objects = new List<LevelObjectPlacement>
            {
                Placement("Starting Step", cube, new Vector3(-0.92f, -0.72f, -0.52f), new Vector3(0.62f, 0.28f, 0.30f)),
                Placement("Middle Step", wedge, new Vector3(-0.05f, -0.38f, -0.50f), new Vector3(0.70f, 0.32f, 0.34f)),
                Placement("Goal Step", cube, new Vector3(0.87f, -0.66f, -0.48f), new Vector3(0.62f, 0.30f, 0.30f)),
                Placement("Bonus Orb", sphere, new Vector3(0.45f, 0.54f, -0.55f), new Vector3(0.36f, 0.36f, 0.36f))
            };
            SetField(level, "objects", objects);
            SetField(level, "lights", new List<LevelLightPlacement> { LightPlacement(lamp, new Vector3(0f, 0.1f, -2.15f)) });

            ChapterDefinition chapter = ScriptableObject.CreateInstance<ChapterDefinition>();
            chapter.name = "Tabletop Dawn";
            SetField(chapter, "id", "tabletop-dawn"); SetField(chapter, "displayName", "Tabletop Dawn");
            SetField(chapter, "levels", new List<LevelDefinition> { level });
            return chapter;
        }

        private GameObject CreateSpotlightTemplate()
        {
            GameObject lamp = new GameObject("Sample Spot Light");
            lamp.transform.SetParent(transform, true);
            Light spot = lamp.AddComponent<Light>();
            lamp.transform.position = new Vector3(10000f, 10000f, 10000f);
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.88f, 0.62f);
            spot.intensity = 3f;
            spot.range = 8f;
            spot.spotAngle = 65f;
            spot.shadows = LightShadows.Hard;
            return lamp;
        }

        private GameObject PrimitiveTemplate(string name, PrimitiveType type, Color color)
        {
            GameObject result = GameObject.CreatePrimitive(type);
            result.name = name;
            result.transform.SetParent(transform, true);
            result.transform.position = new Vector3(10000f, 10000f, 10000f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color;
            Renderer renderer = result.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return result;
        }

        private static LevelObjectPlacement Placement(string id, GameObject prefab, Vector3 position, Vector3 scale)
        {
            var item = new LevelObjectPlacement();
            SetField(item, "placementId", id); SetField(item, "prefab", prefab);
            SetField(item, "wallLocalPosition", position); SetField(item, "localScale", scale);
            SetField(item, "wallLocalEulerAngles", Vector3.zero); SetField(item, "movable", true);
            return item;
        }

        private static LevelLightPlacement LightPlacement(GameObject prefab, Vector3 position)
        {
            var item = new LevelLightPlacement();
            SetField(item, "prefab", prefab); SetField(item, "wallLocalPosition", position);
            SetField(item, "wallLocalEulerAngles", Vector3.zero); SetField(item, "movable", true);
            return item;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) field.SetValue(target, value);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) { EventSystem.current.sendNavigationEvents = false; return; }
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>().sendNavigationEvents = false;
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }
    }

    public static class TrackedHands
    {
        private static int cachedFrame = -1;
        private static IHand leftHand, rightHand;
        private static Hand[] candidates = Array.Empty<Hand>();
        private static float nextScan;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        { cachedFrame = -1; leftHand = rightHand = null; candidates = Array.Empty<Hand>(); nextScan = 0f; }
        public static IHand Find(Handedness side)
        {
            if (cachedFrame == Time.frameCount) return side == Handedness.Left ? leftHand : rightHand;
            cachedFrame = Time.frameCount;
            leftHand = rightHand = null;
            if (Time.unscaledTime >= nextScan)
            {
                candidates = UnityEngine.Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude);
                nextScan = Time.unscaledTime + .5f;
            }
            foreach (var hand in candidates)
            {
                if (hand == null || !hand.isActiveAndEnabled || !hand.IsConnected || !hand.IsTrackedDataValid) continue;
                string path = hand.transform.name;
                for (Transform parent = hand.transform.parent; parent != null; parent = parent.parent) path += "/" + parent.name;
                if (path.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Synthetic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (hand.Handedness == Handedness.Left) leftHand = hand; else rightHand = hand;
            }
            return side == Handedness.Left ? leftHand : rightHand;
        }
        public static bool Pinch(Handedness side, HandFinger finger)
        { IHand hand = Find(side); return hand != null ? hand.GetFingerIsPinching(finger) : ShadowCastXRInput.Pinch(side, finger); }
    }
    [DefaultExecutionOrder(-100)]
    public sealed class XRHandAnchorDriver : MonoBehaviour
    {
        private Transform rightAnchor, leftAnchor;
        private BuildModeManager build;
        private Camera view;
        public void Initialize(Transform right, Transform left, BuildModeManager manager, Camera camera)
        { rightAnchor = right; leftAnchor = left; build = manager; view = camera; }
        private void Update()
        {
            IHand right = TrackedHands.Find(Handedness.Right);
            IHand left = TrackedHands.Find(Handedness.Left);
            bool rightValid = TryPinchPose(right, Handedness.Right, out Pose rightPose) || ShadowCastXRInput.TryControllerPose(false, out rightPose);
            bool leftValid = TryPinchPose(left, Handedness.Left, out Pose leftPose) || ShadowCastXRInput.TryControllerPose(true, out leftPose);
            if (rightValid) rightAnchor.SetPositionAndRotation(rightPose.position, rightPose.rotation);
            if (leftValid) leftAnchor.SetPositionAndRotation(leftPose.position, leftPose.rotation);
            if (build != null && build.InputBridge != null)
                build.InputBridge.ConfigureInputAnchors(rightValid ? rightAnchor : null,
                    leftValid && (TrackedHands.Pinch(Handedness.Left, HandFinger.Index) || ShadowCastXRInput.RawButton("Depth")) ? leftAnchor : null, view);
        }

        private static bool TryPinchPose(IHand hand, Handedness side, out Pose pose)
        {
            pose = default;
            if (hand != null && hand.GetJointPose(HandJointId.HandIndexTip, out Pose index) && hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumb))
            { pose = new Pose((index.position + thumb.position) * .5f, index.rotation); return true; }
            if (!ShadowCastXRInput.TryJoint(side, UnityEngine.XR.Hands.XRHandJointID.IndexTip, out index) ||
                !ShadowCastXRInput.TryJoint(side, UnityEngine.XR.Hands.XRHandJointID.ThumbTip, out thumb)) return false;
            pose = new Pose((index.position + thumb.position) * .5f, index.rotation);
            return true;
        }
    }

    /// <summary>
    /// Configures the Meta passthrough underlay used by the campaign's mixed-reality presentation.
    /// The campaign places its play surface manually, so it does not depend on Scene Capture/MRUK.
    /// </summary>
    internal static class MixedRealityPassthroughBootstrap
    {
        public static void Ensure(GameObject host, Camera xrCamera)
        {
            if (host == null || xrCamera == null) return;
            var state = host.AddComponent<ShadowCastPassthroughState>();
            state.Initialize(xrCamera);
        }
    }
}
