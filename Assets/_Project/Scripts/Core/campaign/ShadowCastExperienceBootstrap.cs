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
using InputDevice = UnityEngine.XR.InputDevice;
using InputDevices = UnityEngine.XR.InputDevices;
using CommonUsages = UnityEngine.XR.CommonUsages;

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
            SetField(level, "characterStartWallLocalPosition", new Vector3(-1.35f, -0.50f, 0.035f));
            SetField(level, "goalWallLocalPosition", new Vector3(1.30f, -0.50f, 0.035f));
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
            spot.intensity = 1800f;
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
            if (EventSystem.current != null) return;
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }
    }

    public static class TrackedHands
    {
        private static int cachedFrame = -1;
        private static IHand leftHand, rightHand;
        public static IHand Find(Handedness side)
        {
            if (cachedFrame == Time.frameCount) return side == Handedness.Left ? leftHand : rightHand;
            cachedFrame = Time.frameCount;
            leftHand = rightHand = null;
            foreach (var hand in UnityEngine.Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude))
            {
                if (!hand.IsConnected || !hand.IsTrackedDataValid) continue;
                string path = hand.transform.name;
                for (Transform parent = hand.transform.parent; parent != null; parent = parent.parent) path += "/" + parent.name;
                if (path.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Synthetic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (hand.Handedness == Handedness.Left) leftHand = hand; else rightHand = hand;
            }
            return side == Handedness.Left ? leftHand : rightHand;
        }
        public static bool Pinch(Handedness side, HandFinger finger)
        { IHand hand = Find(side); return hand != null && hand.GetFingerIsPinching(finger); }
    }
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
            if (right != null && right.GetJointPose(HandJointId.HandIndexTip, out Pose index) && right.GetJointPose(HandJointId.HandThumbTip, out Pose thumb))
            {
                rightAnchor.SetPositionAndRotation((index.position + thumb.position) * .5f, index.rotation);
                if (build != null && build.InputBridge != null)
                    build.InputBridge.ConfigureInputAnchors(rightAnchor, left != null && left.GetIndexFingerIsPinching() ? leftAnchor : null, view);
            }
            if (left != null && left.GetJointPose(HandJointId.HandIndexTip, out Pose point)) leftAnchor.SetPositionAndRotation(point.position, point.rotation);
        }
    }

    /// <summary>Gaze plus controller-trigger UI, with a full world-space flow and desktop fallback.</summary>
    public sealed class ShadowCastHeadsetUI : MonoBehaviour
    {
        private const float ArtWidth = 1920f;
        private const float ArtHeight = 1080f;
        private CampaignFlowController flow;
        private BuildModeManager build;
        private Camera view;
        private Canvas canvas;
        private GraphicRaycaster raycaster;
        private RawImage artwork;
        private readonly List<Button> hitboxes = new List<Button>();
        private readonly Dictionary<Button, float> movementHitboxes = new Dictionary<Button, float>();
        private readonly List<RaycastResult> rayResults = new List<RaycastResult>();
        private InputDevice rightController;
        private bool triggerWasDown;
        private bool toggleWasDown;
        private bool pickerWasDown;
        private bool jumpWasDown;
        private bool dashWasDown;
        private bool canvasVisible = true;
        private float screenMoveInput;
        private WallPlatformerController2D character;
        private CampaignGameplayHUD gameplayHud;
        private WallPlacementController wallPlacement;
        private GameObject blockPrefab, rampPrefab, orbPrefab;
        private CampaignScreen currentScreen;
        private float initialCanvasPlacementDeadline;

        public void Initialize(CampaignFlowController controller, BuildModeManager manager, Camera playerCamera, ShadowCastExperienceBootstrap experience, CampaignGameplayHUD hud, WallPlacementController placement)
        {
            flow = controller; build = manager; view = playerCamera; gameplayHud = hud; wallPlacement = placement;
            if (experience != null)
            {
                blockPrefab = experience.BlockPrefab; rampPrefab = experience.RampPrefab; orbPrefab = experience.OrbPrefab;
            }
            CreateCanvas();
            // XR tracking commonly updates the eye camera after scene/runtime Awake. Keep
            // settling the first menu briefly so it cannot be stranded at the rig origin.
            initialCanvasPlacementDeadline = Time.unscaledTime + 1.5f;
            flow.ScreenChanged += OnScreen;
            flow.MessageChanged += OnMessage;
            OnScreen(flow.CurrentScreen);
        }

        private void CreateCanvas()
        {
            GameObject go = new GameObject("Figma Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.001f;
            canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = view;
            canvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 1f;
            raycaster = go.GetComponent<GraphicRaycaster>();
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(ArtWidth, ArtHeight);
            GameObject imageObject = new GameObject("Figma Artwork", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(go.transform, false);
            artwork = imageObject.GetComponent<RawImage>(); artwork.raycastTarget = true;
            RectTransform imageRect = artwork.rectTransform;
            imageRect.anchorMin = Vector2.zero; imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero; imageRect.offsetMax = Vector2.zero;
        }

        private void OnScreen(CampaignScreen screen)
        {
            currentScreen = screen;
            if (wallPlacement != null)
            {
                wallPlacement.SetPlacementActive(screen == CampaignScreen.WallPlacement);
                if (screen == CampaignScreen.WallPlacement && flow.SelectedLevel != null)
                    wallPlacement.SetPreviewSize(flow.SelectedLevel.WallWidth, flow.SelectedLevel.WallHeight);
            }
            PositionCanvasInView();
            ClearHitboxes();
            switch (screen)
            {
                case CampaignScreen.MainMenu:
                    ShowArtwork("Menu Page");
                    AddHitbox(290, 280, 360, 560, () => flow.OpenChapterSelect());
                    AddHitbox(660, 310, 550, 290, ShowQuickMode);
                    AddHitbox(1210, 275, 430, 430, ShowQuickMode);
                    AddHitbox(1210, 710, 430, 140, () => flow.OpenChapterSelect());
                    break;
                case CampaignScreen.ChapterSelect:
                    ShowArtwork("Chapter Page");
                    AddHitbox(285, 425, 395, 445, () => flow.SelectChapter(0));
                    AddHitbox(145, 90, 370, 85, flow.ReturnToMainMenu);
                    break;
                case CampaignScreen.LevelSelect:
                    ShowArtwork("Level Page");
                    AddHitbox(445, 455, 365, 225, () => flow.SelectLevel(0));
                    AddHitbox(150, 90, 200, 80, flow.BackToChapterSelect);
                    break;
                case CampaignScreen.WallPlacement:
                    ShowArtwork("Setting a game area");
                    AddHitbox(215, 245, 1500, 600, PlaceSampleWall);
                    AddHitbox(135, 145, 290, 95, flow.BackToLevelSelect);
                    break;
                case CampaignScreen.Build:
                    ShowObjectSelection();
                    break;
                case CampaignScreen.Baking:
                    ShowArtwork("Placing object Shadows to create Path");
                    break;
                case CampaignScreen.Play:
                    ShowArtwork("Object shadow Path");
                    AddHitbox(600, 865, 95, 170, () => { }, -1f);
                    AddHitbox(695, 865, 105, 170, () => { }, 1f);
                    AddHitbox(990, 870, 190, 190, () => character?.RequestJump());
                    AddHitbox(65, 85, 300, 100, flow.ReturnToBuild);
                    break;
                case CampaignScreen.LevelComplete:
                    ShowArtwork("Chapter Page");
                    AddHitbox(305, 790, 350, 72, flow.ContinueAfterCompletion);
                    AddHitbox(145, 90, 370, 85, flow.ReturnToMainMenu);
                    break;
                case CampaignScreen.LevelFailed:
                    ShowArtwork("Object shadow Path");
                    AddHitbox(65, 85, 300, 100, flow.RetryLevel);
                    AddHitbox(990, 870, 190, 190, () => character?.RequestJump());
                    break;
            }
            canvasVisible = screen != CampaignScreen.Play;
            canvas.gameObject.SetActive(canvasVisible);
            if (screen == CampaignScreen.Play) character = FindAnyObjectByType<WallPlatformerController2D>();
        }

        private void ShowObjectSelection()
        {
            ShowArtwork("Selecting Your Objects");
            AddHitbox(275, 300, 300, 500, () => SpawnAndHide(PrimitiveType.Cube, new Vector3(0.62f, 0.28f, 0.30f), null, -0.82f));
            AddHitbox(575, 300, 280, 500, () => SpawnAndHide(PrimitiveType.Cube, new Vector3(0.70f, 0.32f, 0.34f), new Vector3(0f, 0f, 22f), -0.42f));
            AddHitbox(855, 300, 280, 500, () => SpawnAndHide(PrimitiveType.Sphere, Vector3.one * 0.36f, null, 0f));
            AddHitbox(1135, 300, 285, 500, () => SpawnAndHide(PrimitiveType.Cube, new Vector3(0.62f, 0.30f, 0.30f), null, 0.42f));
            AddHitbox(1420, 300, 290, 500, () => SpawnAndHide(PrimitiveType.Cube, new Vector3(0.55f, 0.40f, 0.32f), new Vector3(0f, 0f, -18f), 0.82f));
        }

        private void ShowQuickMode()
        {
            ClearHitboxes();
            currentScreen = CampaignScreen.MainMenu;
            ShowArtwork("Quick Mode");
            AddHitbox(500, 425, 280, 470, StartQuickLevel);
            AddHitbox(820, 425, 280, 470, StartQuickLevel);
            AddHitbox(1140, 425, 280, 470, StartQuickLevel);
            AddHitbox(65, 125, 300, 100, flow.ReturnToMainMenu);
        }

        private void StartQuickLevel()
        {
            flow.OpenChapterSelect();
            flow.SelectChapter(0);
            flow.SelectLevel(0);
        }

        private void ShowArtwork(string resourceName)
        {
            Texture2D texture = Resources.Load<Texture2D>($"FigmaScreens/{resourceName}");
            if (texture == null)
            {
                Debug.LogError($"Missing supplied Figma screen export: Assets/Resources/FigmaScreens/{resourceName}.png", this);
                canvas.gameObject.SetActive(false);
                return;
            }
            canvas.gameObject.SetActive(true);
            artwork.texture = texture;
            RectTransform rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(ArtWidth, ArtHeight);
            artwork.uvRect = texture.height > ArtHeight
                ? new Rect(0f, 1f - ArtHeight / texture.height, 1f, ArtHeight / texture.height)
                : new Rect(0f, 0f, 1f, 1f);
        }

        private void AddHitbox(float x, float y, float width, float height, Action action, float movement = 0f)
        {
            GameObject target = new GameObject("Figma hotspot", typeof(RectTransform), typeof(Image), typeof(Button));
            target.transform.SetParent(canvas.transform, false);
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x - ArtWidth * 0.5f, ArtHeight * 0.5f - y);
            Image image = target.GetComponent<Image>(); image.color = new Color(0f, 0f, 0f, 0f); image.raycastTarget = true;
            Button button = target.GetComponent<Button>(); button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(() => action());
            if (movement != 0f) movementHitboxes[button] = movement;
            hitboxes.Add(button);
        }

        private void PlaceSampleWall()
        {
            // Prefer a real vertical collider under the center reticle when room geometry
            // is available. The deterministic virtual wall keeps desktop testing and
            // headsets without Scene Capture from becoming stuck at this step.
            if (wallPlacement != null && wallPlacement.Confirm(flow)) return;
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            Pose pose = new Pose(view.transform.position + forward * 2.8f, Quaternion.LookRotation(-forward, Vector3.up));
            flow.ConfirmWallPlacement(pose);
        }

        private void SpawnPrimitive(PrimitiveType type, Vector3? scale = null, Vector3? rotation = null, float wallX = 0f)
        {
            GameObject prefab = rotation.HasValue && rampPrefab != null ? rampPrefab : (type == PrimitiveType.Sphere ? orbPrefab : blockPrefab);
            GameObject item = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(type);
            item.name = type == PrimitiveType.Cube ? "Buildable Block" : "Buildable Orb";
            Transform wall = build.TargetWallPlane;
            item.transform.position = wall != null
                ? wall.TransformPoint(new Vector3(wallX, 0f, -0.52f))
                : view.transform.position + view.transform.forward * 1.8f + view.transform.right * wallX - view.transform.up * 0.12f;
            item.transform.rotation = rotation.HasValue ? Quaternion.Euler(rotation.Value) : Quaternion.identity;
            item.transform.localScale = scale ?? (prefab != null ? prefab.transform.localScale * 0.32f : Vector3.one * 0.32f);
            int casterLayer = LayerMask.NameToLayer("ShadowCaster");
            SetLayerRecursively(item.transform, casterLayer >= 0 ? casterLayer : 7);
            if (prefab == null)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = type == PrimitiveType.Cube ? new Color(0.16f, 0.68f, 0.85f) : new Color(0.7f, 0.39f, 0.9f);
                item.GetComponent<Renderer>().sharedMaterial = material;
            }
            build.SpawnObject(item, item.transform.position, item.transform.rotation);
            Destroy(item);
        }

        private void SpawnAndHide(PrimitiveType type, Vector3 scale, Vector3? rotation, float wallX)
        {
            SpawnPrimitive(type, scale, rotation, wallX);
            SetCanvasVisible(false);
        }

        private void SetCanvasVisible(bool visible)
        {
            canvasVisible = visible;
            if (visible) PositionCanvasInView();
            canvas.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (view == null || canvas == null) return;
            PollOverlayToggle();
            if (canvasVisible || (gameplayHud != null && gameplayHud.HasInteractiveUI))
            {
                PollPointer();
            }
            if (flow.CurrentScreen == CampaignScreen.Play && (gameplayHud == null || !gameplayHud.IsPaused)) PollPlayInput();
        }

        private void LateUpdate()
        {
            if (view != null && canvas != null && canvasVisible && Time.unscaledTime <= initialCanvasPlacementDeadline)
                PositionCanvasInView();
        }

        private void PollOverlayToggle()
        {
            Keyboard keyboard = Keyboard.current;
            bool overlayKeyDown = keyboard != null && keyboard.tabKey.isPressed;
            bool pauseKeyDown = keyboard != null && keyboard.escapeKey.isPressed;
            bool pickerDown = keyboard != null && keyboard.pKey.isPressed;
            bool menuButtonDown = TrackedHands.Pinch(Handedness.Right, HandFinger.Pinky);
            pickerDown |= TrackedHands.Pinch(Handedness.Right, HandFinger.Ring);
            bool pauseDown = pauseKeyDown || (flow.CurrentScreen == CampaignScreen.Play && menuButtonDown);
            bool overlayDown = overlayKeyDown || (flow.CurrentScreen == CampaignScreen.Build && menuButtonDown);
            if (pickerDown && !pickerWasDown && flow.CurrentScreen == CampaignScreen.Build)
            {
                ClearHitboxes();
                ShowObjectSelection();
                SetCanvasVisible(true);
            }
            if (pauseDown && !toggleWasDown)
            {
                gameplayHud?.TogglePause();
            }
            else if (overlayDown && !toggleWasDown)
            {
                if (flow.CurrentScreen == CampaignScreen.Build || flow.CurrentScreen == CampaignScreen.Play)
                {
                    if (!canvasVisible)
                    {
                        if (flow.CurrentScreen == CampaignScreen.Build)
                        {
                            ClearHitboxes();
                            ShowArtwork("Adjusting Your Game Ground");
                            AddHitbox(1580, 80, 340, 150, flow.BakeLevel);
                            AddHitbox(300, 80, 440, 150, flow.BackToLevelSelect);
                        }
                        SetCanvasVisible(true);
                    }
                    else SetCanvasVisible(false);
                }
            }
            toggleWasDown = pauseDown || overlayDown;
            pickerWasDown = pickerDown;
        }

        private void PositionCanvasInView()
        {
            Transform cameraTransform = view.transform;
            canvas.transform.position = cameraTransform.position + cameraTransform.forward * 2f;
            // GraphicRaycaster considers this orientation front-facing for a world-space
            // canvas viewed by the assigned event camera.
            canvas.transform.rotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
        }

        private void PollPointer()
        {
            bool handTrigger = TrackedHands.Pinch(Handedness.Right, HandFinger.Index);
            bool controllerTrigger = GetRightControllerTrigger();
            bool trigger = handTrigger || controllerTrigger;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            bool mouseClick = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool mouseDown = mouse != null && mouse.leftButton.isPressed;
            bool keyboardClick = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame);
            bool pointerDown = trigger || mouseDown;
            bool click = (trigger && !triggerWasDown) || mouseClick || keyboardClick;
            triggerWasDown = trigger;
            Vector2 pointerPosition = mouseClick || mouseDown ? mouse.position.ReadValue() : view.pixelRect.center;
            if (!mouseClick && !mouseDown && handTrigger)
            {
                IHand hand = TrackedHands.Find(Handedness.Right);
                Transform target = gameplayHud != null && gameplayHud.IsPaused
                    ? gameplayHud.GetComponentInChildren<Canvas>()?.transform : canvas.transform;
                if (hand == null || target == null || !hand.GetPointerPose(out Pose pointing)) return;
                Ray ray = new Ray(pointing.position, pointing.rotation * Vector3.forward);
                if (!new Plane(target.forward, target.position).Raycast(ray, out float distance)) return;
                Vector3 screen = view.WorldToScreenPoint(ray.GetPoint(distance));
                if (screen.z <= 0f) return;
                pointerPosition = screen;
            }
            if (!view.pixelRect.Contains(pointerPosition))
            {
                screenMoveInput = 0f;
                return;
            }
            rayResults.Clear();
            var pointer = new PointerEventData(EventSystem.current) { position = pointerPosition };
            EventSystem.current.RaycastAll(pointer, rayResults);
            Button hovered = null;
            for (int i = 0; i < rayResults.Count; i++)
            {
                Button button = rayResults[i].gameObject.GetComponentInParent<Button>();
                if (button != null && button.interactable) { hovered = button; break; }
            }
            if (click && hovered != null) hovered.onClick.Invoke();
            else if (click && currentScreen == CampaignScreen.Build) SetCanvasVisible(false);
            screenMoveInput = currentScreen == CampaignScreen.Play && pointerDown && hovered != null && movementHitboxes.TryGetValue(hovered, out float movement)
                ? movement
                : 0f;
        }

        private bool GetRightControllerTrigger()
        {
            if (!rightController.isValid)
                rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            if (!rightController.isValid ||
                !rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed))
            {
                return false;
            }

            return pressed;
        }

        private void PollPlayInput()
        {
            if (character == null) character = FindAnyObjectByType<WallPlatformerController2D>();
            if (character == null) return;
            float axis = 0f;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) axis -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) axis += 1f;
            }
            bool jumpDown = keyboard != null && (keyboard.spaceKey.isPressed || keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed);
            bool dashDown = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed || keyboard.eKey.isPressed);
            IHand leftHand = TrackedHands.Find(Handedness.Left);
            if (leftHand != null && leftHand.GetIndexFingerIsPinching() && leftHand.GetRootPose(out Pose wrist))
                axis = Mathf.Clamp((view.transform.InverseTransformPoint(wrist.position).x + 0.25f) / 0.18f, -1f, 1f);
            if (Mathf.Abs(screenMoveInput) > 0.01f) axis = screenMoveInput;
            jumpDown |= TrackedHands.Pinch(Handedness.Right, HandFinger.Middle);
            dashDown |= TrackedHands.Pinch(Handedness.Right, HandFinger.Ring);
            character.SetMovementInput(axis);
            if (jumpDown && !jumpWasDown) character.RequestJump();
            else if (!jumpDown && jumpWasDown) character.ReleaseJump();
            if (dashDown && !dashWasDown) character.RequestDash();
            jumpWasDown = jumpDown;
            dashWasDown = dashDown;
        }

        private void ClearHitboxes()
        {
            foreach (Button button in hitboxes) if (button != null) Destroy(button.gameObject);
            hitboxes.Clear();
            movementHitboxes.Clear();
        }
        private static void SetLayerRecursively(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++) SetLayerRecursively(target.GetChild(i), layer);
        }
        private void OnMessage(string message) { Debug.Log(message, this); }
        private void OnDestroy() { if (flow != null) { flow.ScreenChanged -= OnScreen; flow.MessageChanged -= OnMessage; } }
    }

    /// <summary>
    /// Configures the Meta passthrough underlay used by the campaign's mixed-reality presentation.
    /// The campaign places its play surface manually, so it does not depend on Scene Capture/MRUK.
    /// </summary>
    internal static class MixedRealityPassthroughBootstrap
    {
        public static void Ensure(GameObject host, Camera xrCamera)
        {
            if (host == null) return;

            // An underlay is only visible through transparent pixels in the eye camera.
            if (xrCamera != null)
            {
                xrCamera.clearFlags = CameraClearFlags.SolidColor;
                xrCamera.backgroundColor = Color.clear;
            }

            OVRManager manager = UnityEngine.Object.FindAnyObjectByType<OVRManager>();
            if (manager == null) manager = host.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;

            OVRPassthroughLayer layer = UnityEngine.Object.FindAnyObjectByType<OVRPassthroughLayer>();
            if (layer == null) layer = host.AddComponent<OVRPassthroughLayer>();
            layer.hidden = false;
            layer.textureOpacity = 1f;
            layer.edgeRenderingEnabled = false;
        }
    }
}
