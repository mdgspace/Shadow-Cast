using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Oculus.Interaction.Input;
using ShadowCast.Core.BuildMode;
using HandFinger = Oculus.Interaction.Input.HandFinger;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Individual Figma elements with world-space interaction for hands and controllers.</summary>
    public sealed class ShadowCastHeadsetUI : MonoBehaviour
    {
        private CampaignFlowController flow;
        private BuildModeManager build;
        private Camera view;
        private Canvas canvas;
        private Transform pageRoot;
        private CampaignGameplayHUD gameplayHud;
        private WallPlacementController wallPlacement;
        private GameObject blockPrefab, rampPrefab, orbPrefab;
        private WallPlatformerController2D character;
        private ShadowCastUIInteractor interaction;
        private CampaignScreen currentScreen;
        private bool canvasVisible, menuWasDown, pickerWasDown, jumpWasDown, dashWasDown;
        private string lastMessage;
        public Canvas MenuCanvas => canvas;
        public ShadowCastUIInteractor Interaction => interaction;
        public bool BlocksBuildInput => currentScreen != CampaignScreen.Build || canvasVisible || (gameplayHud != null && gameplayHud.IsPaused) || (interaction != null && interaction.IsPointingAtUI);

        public void Initialize(CampaignFlowController controller, BuildModeManager manager, Camera camera, ShadowCastExperienceBootstrap experience, CampaignGameplayHUD hud, WallPlacementController placement)
        {
            flow = controller; build = manager; view = camera; gameplayHud = hud; wallPlacement = placement;
            if (experience != null) { blockPrefab = experience.BlockPrefab; rampPrefab = experience.RampPrefab; orbPrefab = experience.OrbPrefab; }
            var root = new GameObject("Campaign Navigation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            ShadowCastUIStyle.ConfigureCanvas(canvas, view, 1600f, ShadowCastUIStyle.MenuWidth);
            canvas.sortingOrder = 100;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1600, 1000);
            interaction = gameObject.AddComponent<ShadowCastUIInteractor>();
            interaction.Initialize(view);
            interaction.RegisterCanvas(canvas);
            interaction.RegisterCanvas(hud.UICanvas);
            hud.MenuRequested += OpenBuildMenu;
            flow.ScreenChanged += OnScreen;
            flow.MessageChanged += OnMessage;
            OnScreen(flow.CurrentScreen);
        }

        private void Page(string heading, string subtitle)
        {
            interaction.CancelAllPointers();
            if (pageRoot != null) { pageRoot.gameObject.SetActive(false); Destroy(pageRoot.gameObject); }
            pageRoot = new GameObject("Menu Content", typeof(RectTransform)).transform;
            pageRoot.SetParent(canvas.transform, false);
            pageRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(1600, 1000);
            if (!string.IsNullOrEmpty(heading)) ShadowCastFigmaAssets.Image("UmbraLogo", pageRoot, new Vector2(-560, 412), new Vector2(280, 60));
            ShadowCastUIStyle.Text("Navigation Title", pageRoot, new Vector2(130, 405), new Vector2(1000, 110), heading, 52);
            ShadowCastUIStyle.Text("Navigation Subtitle", pageRoot, new Vector2(0, 310), new Vector2(1450, 95), subtitle, 28, new Color(.75f, .9f, .94f));
            ShadowCastUIStyle.Text("Gesture Guidance", pageRoot, new Vector2(0, -405), new Vector2(1500, 65), "POINT to highlight  /  PINCH + RELEASE to select", 29, ShadowCastUIStyle.Accent);
            ShadowCastUIStyle.Text("Controller Guidance", pageRoot, new Vector2(0, -462), new Vector2(1500, 45), "Either hand works  /  Controllers: aim, squeeze trigger, then release", 24);
            SetCanvasVisible(true);
        }

        private Button Button(string label, float x, float y, float width, Action action, bool enabled = true)
        {
            Button button = ShadowCastUIStyle.Button(label, pageRoot, new Vector2(x, y), new Vector2(width, 112), action);
            button.interactable = enabled;
            return button;
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
            switch (screen)
            {
                case CampaignScreen.MainMenu:
                    Page("", "");
                    ShadowCastFigmaAssets.Image("UmbraLogo", pageRoot, new Vector2(-365, 405), new Vector2(500, 106));
                    ShadowCastFigmaAssets.Image("Tagline", pageRoot, new Vector2(-365, 322), new Vector2(435, 20));
                    var story = ShadowCastUIStyle.ArtworkButton("StoryCard", "STORY MODE", pageRoot, new Vector2(-385, -15), new Vector2(360, 479), flow.OpenChapterSelect);
                    ShadowCastFigmaAssets.Image("StoryIllustration", story.transform, new Vector2(0, 80), new Vector2(355, 310));
                    ShadowCastFigmaAssets.Image("StoryHeading", story.transform, new Vector2(0, -105), new Vector2(265, 44));
                    ShadowCastFigmaAssets.Image("StoryCaption", story.transform, new Vector2(0, -175), new Vector2(184, 43));
                    ShadowCastUIStyle.ArtworkButton("QuickPlay", "QUICK PLAY", pageRoot, new Vector2(190, 86), new Vector2(660, 332), StartQuickLevel);
                    if (flow.Progress.HasSave) Button("CONTINUE SAVED GAME", 190, -180, 660, () => flow.ContinueFromLocalSave());
                    break;
                case CampaignScreen.ChapterSelect: ShowChapters(0); break;
                case CampaignScreen.LevelSelect: ShowLevels(0); break;
                case CampaignScreen.WallPlacement:
                    Page("CHOOSE YOUR WALL", "Look toward a clear wall, then confirm your play area.");
                    Button("CONFIRM WALL", 0, 70, 880, PlaceSampleWall);
                    Button("BACK TO LEVELS", 0, -115, 680, flow.BackToLevelSelect);
                    break;
                case CampaignScreen.Build: OpenBuildMenu(); break;
                case CampaignScreen.Baking: Page("CREATING YOUR PATH", "Baking the shadows. Your game will start shortly."); break;
                case CampaignScreen.Play:
                    SetCanvasVisible(false);
                    character = FindAnyObjectByType<WallPlatformerController2D>();
                    break;
                case CampaignScreen.LevelComplete:
                    Page("LEVEL COMPLETE", $"SCORE  {flow.LastAwardedScore:N0}     BEST  {flow.LastBestScore:N0}     TOTAL  {flow.TotalScore:N0}");
                    Button("CONTINUE", -345, 65, 630, flow.ContinueAfterCompletion);
                    Button("PLAY AGAIN", 345, 65, 630, flow.RetryLevel);
                    Button("MAIN MENU", 0, -140, 680, flow.ReturnToMainMenu);
                    break;
                case CampaignScreen.LevelFailed:
                    Page("TRY AGAIN", string.IsNullOrEmpty(lastMessage) ? "Rebuild your shadow path and try again." : lastMessage);
                    Button("RETRY LEVEL", 0, 65, 800, flow.RetryLevel);
                    Button("BACK TO LEVELS", 0, -125, 680, flow.BackToLevelSelect);
                    break;
            }
            PositionCanvasInView();
            interaction.RefreshTargets();
        }

        private void ShowChapters(int page)
        {
            Page("SELECT CHAPTER", "Choose a story to continue.");
            AddPagedEntries(page, flow.Chapters.Count,
                i => flow.Chapters[i] != null ? flow.Chapters[i].DisplayName : "Unavailable",
                i => flow.IsLevelUnlocked(i, 0), i => flow.SelectChapter(i), ShowChapters, flow.ReturnToMainMenu, true);
        }
        private void ShowLevels(int page)
        {
            Page("CHOOSE A LEVEL", flow.SelectedChapter != null ? flow.SelectedChapter.DisplayName : "Choose your next shadow puzzle.");
            int count = flow.SelectedChapter != null ? flow.SelectedChapter.Levels.Count : 0;
            AddPagedEntries(page, count,
                i => flow.SelectedChapter.Levels[i] != null ? flow.SelectedChapter.Levels[i].DisplayName : "Unavailable",
                i => flow.IsLevelUnlocked(flow.SelectedChapterIndex, i), i => flow.SelectLevel(i), ShowLevels, flow.BackToChapterSelect);
        }
        private void AddPagedEntries(int page, int count, Func<int, string> label, Func<int, bool> available, Action<int> choose, Action<int> changePage, Action back, bool chapters = false)
        {
            int pageSize = chapters ? 3 : 6;
            for (int index = page * pageSize; index < Mathf.Min(count, page * pageSize + pageSize); index++)
            {
                int captured = index, slot = index % pageSize;
                bool unlocked = available(index);
                Vector2 size = chapters ? new Vector2(420, 470) : new Vector2(420, 225);
                Vector2 position = new Vector2((slot % 3 - 1) * 460, chapters ? 0 : 140 - (slot / 3) * 255);
                Button card = ShadowCastUIStyle.ArtworkButton("StoryCard", label(index), pageRoot, position, size, () => choose(captured));
                card.interactable = unlocked;
                card.transform.Find("Label").gameObject.SetActive(false);
                Image illustration = ShadowCastFigmaAssets.Image("StoryIllustration", card.transform,
                    chapters ? new Vector2(0, 0) : new Vector2(110, -5), chapters ? new Vector2(398, 212) : new Vector2(200, 122));
                illustration.sprite = ShadowCastFigmaAssets.Illustration(chapters, index);
                ShadowCastUIStyle.Text("Entry Title", card.transform, chapters ? new Vector2(0, 175) : new Vector2(-80, 28),
                    chapters ? new Vector2(385, 85) : new Vector2(230, 95), label(index), chapters ? 30 : 27);
                ShadowCastUIStyle.Text("Entry State", card.transform, chapters ? new Vector2(0, -180) : new Vector2(-100, -68),
                    chapters ? new Vector2(385, 65) : new Vector2(200, 48), unlocked ? (chapters ? "START CHAPTER  →" : "PLAY  →") : "LOCKED", 24, ShadowCastUIStyle.Accent);
            }
            Button("BACK", 0, -310, 320, back);
            if (page > 0) Button("PREVIOUS", -490, -310, 320, () => changePage(page - 1));
            if ((page + 1) * pageSize < count) Button("NEXT", 490, -310, 320, () => changePage(page + 1));
            interaction.RefreshTargets();
        }

        public void OpenBuildMenu()
        {
            if (flow.CurrentScreen != CampaignScreen.Build) return;
            Page("BUILD YOUR SHADOW PATH", "Point and pinch to add a shape. Close the menu to grab and edit objects.");
            Button("BAKE & PLAY", 0, 165, 1230, flow.BakeLevel);
            Button("ADD BLOCK", -420, 15, 380, () => SpawnAndHide(PrimitiveType.Cube, new Vector3(.62f, .28f, .30f), null, -.82f));
            Button("ADD RAMP", 0, 15, 380, () => SpawnAndHide(PrimitiveType.Cube, new Vector3(.70f, .32f, .34f), new Vector3(0, 0, 22), -.42f));
            Button("ADD ORB", 420, 15, 380, () => SpawnAndHide(PrimitiveType.Sphere, Vector3.one * .36f, null, .3f));
            Button("UNDO", -420, -130, 380, build.Undo);
            Button("REDO", 0, -130, 380, build.Redo);
            Button("CLOSE MENU", 420, -130, 380, () => SetCanvasVisible(false));
            Button("BACK TO LEVELS", 0, -285, 680, flow.BackToLevelSelect);
            interaction.RefreshTargets();
        }
        private void StartQuickLevel() { flow.OpenChapterSelect(); flow.SelectChapter(0); flow.SelectLevel(0); }
        private void PlaceSampleWall()
        {
            if (wallPlacement != null && wallPlacement.Confirm(flow)) return;
            Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            flow.ConfirmWallPlacement(new Pose(view.transform.position + forward * 2.8f, Quaternion.LookRotation(forward, Vector3.up)));
        }
        private void SpawnAndHide(PrimitiveType type, Vector3 scale, Vector3? rotation, float x)
        { SpawnPrimitive(type, scale, rotation, x); SetCanvasVisible(false); }
        private void SetCanvasVisible(bool visible)
        {
            canvasVisible = visible;
            canvas.gameObject.SetActive(visible);
            gameplayHud?.SetNavigationMenuVisible(visible);
            if (visible) PositionCanvasInView();
            interaction?.CancelAllPointers();
        }
        private void PositionCanvasInView()
        {
            canvas.transform.SetPositionAndRotation(view.transform.position + view.transform.forward * ShadowCastUIStyle.Distance, view.transform.rotation);
        }
        private void LateUpdate()
        {
            if (view != null && canvasVisible && (int)currentScreen <= (int)CampaignScreen.LevelSelect) PositionCanvasInView();
        }
        private void Update()
        {
            if (flow == null) return;
            interaction.ModalCanvas = gameplayHud.IsPaused ? gameplayHud.UICanvas : null;
            var keyboard = Keyboard.current;
            bool menu = ShadowCastXRInput.RawButton("Menu") || TrackedHands.Pinch(Handedness.Right, HandFinger.Pinky) ||
                (keyboard != null && (keyboard.tabKey.isPressed || keyboard.escapeKey.isPressed));
            bool picker = ShadowCastXRInput.RawButton("Picker") || (keyboard != null && keyboard.pKey.isPressed);
            if (menu && !menuWasDown)
            {
                if (flow.CurrentScreen == CampaignScreen.Play) gameplayHud.TogglePause();
                else if (flow.CurrentScreen == CampaignScreen.Build) { if (canvasVisible) SetCanvasVisible(false); else OpenBuildMenu(); }
                else PositionCanvasInView();
            }
            if (picker && !pickerWasDown) OpenBuildMenu();
            menuWasDown = menu; pickerWasDown = picker;
            if (flow.CurrentScreen == CampaignScreen.Play && !gameplayHud.IsPaused) PollPlayInput();
        }
        private void SpawnPrimitive(PrimitiveType type, Vector3? scale = null, Vector3? rotation = null, float wallX = 0f)
        {
            GameObject prefab = rotation.HasValue && rampPrefab != null ? rampPrefab : (type == PrimitiveType.Sphere ? orbPrefab : blockPrefab);
            GameObject item = prefab != null ? Instantiate(prefab) : GameObject.CreatePrimitive(type);
            item.name = type == PrimitiveType.Cube ? "Buildable Block" : "Buildable Orb";
            Transform wall = build.TargetWallPlane;
            item.transform.position = wall != null
                ? wall.position + wall.rotation * new Vector3(wallX, 0f, -0.52f)
                : view.transform.position + view.transform.forward * 1.8f + view.transform.right * wallX - view.transform.up * 0.12f;
            item.transform.rotation = (wall != null ? wall.rotation : Quaternion.identity) * (rotation.HasValue ? Quaternion.Euler(rotation.Value) : Quaternion.identity);
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

        private void PollPlayInput()
        {
            if (character == null) character = FindAnyObjectByType<WallPlatformerController2D>();
            if (character == null) return;
            float axis = ShadowCastXRInput.Move;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) axis -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) axis += 1f;
            }
            bool jumpDown = keyboard != null && (keyboard.spaceKey.isPressed || keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed);
            bool dashDown = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed || keyboard.eKey.isPressed);
            jumpDown |= ShadowCastXRInput.RawButton("Jump");
            dashDown |= ShadowCastXRInput.RawButton("Dash");
            if (gameplayHud != null && Mathf.Abs(gameplayHud.MovementInput) > .01f) axis = gameplayHud.MovementInput;
            character.SetMovementInput(axis);
            if (jumpDown && !jumpWasDown) character.RequestJump();
            else if (!jumpDown && jumpWasDown) character.ReleaseJump();
            if (dashDown && !dashWasDown) character.RequestDash();
            jumpWasDown = jumpDown;
            dashWasDown = dashDown;
        }


        private static void SetLayerRecursively(Transform target, int layer)
        { target.gameObject.layer = layer; for (int i = 0; i < target.childCount; i++) SetLayerRecursively(target.GetChild(i), layer); }
        private void OnMessage(string message) { lastMessage = message; }
        private void OnDestroy()
        {
            if (flow != null) { flow.ScreenChanged -= OnScreen; flow.MessageChanged -= OnMessage; }
            if (gameplayHud != null) gameplayHud.MenuRequested -= OpenBuildMenu;
        }
    }
}
