using System;
using UnityEngine;
using UnityEngine.UI;
using ShadowCast.Core.BuildMode;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Runtime world-space HUD plus pause/settings/save menu shared by desktop and XR.</summary>
    public sealed class CampaignGameplayHUD : MonoBehaviour
    {
        private const float Width = 1200f;
        private const float Height = 700f;
        private static readonly Color PanelColor = new Color(0.035f, 0.045f, 0.075f, 0.94f);
        private static readonly Color Cyan = new Color(0.20f, 0.90f, 1f, 1f);
        private static readonly Color Pink = new Color(1f, 0.35f, 0.78f, 1f);

        private CampaignFlowController flow;
        private CampaignProgressStore progress;
        private Camera view;
        private Canvas canvas;
        private GameObject gameplayRoot;
        private GameObject scoreHintRoot;
        private GameObject continueRoot;
        private GameObject pauseRoot;
        private GameObject pausePage;
        private GameObject settingsPage;
        private GameObject hintPanel;
        private GameObject completionRoot;
        private GameObject warningPanel;
        private Text doubleJumpLabel;
        private Text dashLabel;
        private Text scoreLabel;
        private Text hintLabel;
        private Text completionLabel;
        private Text warningLabel;
        private Text settingsSummary;
        private Text statusLabel;
        private Button hintButton;
        private WallPlatformerController2D character;
        private ObjectManipulator objectManipulator;
        private float previousTimeScale = 1f;
        private float hideHintAt;
        private float hideWarningAt;

        public bool IsPaused { get; private set; }
        public bool HasInteractiveUI => canvas != null && canvas.gameObject.activeInHierarchy &&
            ((gameplayRoot != null && gameplayRoot.activeInHierarchy) || (scoreHintRoot != null && scoreHintRoot.activeInHierarchy) ||
             (continueRoot != null && continueRoot.activeInHierarchy) ||
             (pauseRoot != null && pauseRoot.activeInHierarchy));

        public void Initialize(CampaignFlowController controller, Camera playerCamera)
        {
            flow = controller;
            progress = flow != null ? flow.Progress : null;
            view = playerCamera;
            BuildUI();
            objectManipulator = FindAnyObjectByType<ObjectManipulator>();
            if (objectManipulator != null) objectManipulator.OnManipulationBlocked += ShowManipulationWarning;
            if (flow != null)
            {
                flow.ScreenChanged += HandleScreenChanged;
                flow.RunScoreChanged += HandleRunScoreChanged;
                flow.HintRevealed += HandleHintRevealed;
            }
            if (progress != null)
            {
                progress.Saved += HandleSaved;
                progress.Loaded += RefreshSettings;
            }
            HandleScreenChanged(flow != null ? flow.CurrentScreen : CampaignScreen.MainMenu);
        }

        private void OnDestroy()
        {
            if (flow != null)
            {
                flow.ScreenChanged -= HandleScreenChanged;
                flow.RunScoreChanged -= HandleRunScoreChanged;
                flow.HintRevealed -= HandleHintRevealed;
            }
            if (progress != null)
            {
                progress.Saved -= HandleSaved;
                progress.Loaded -= RefreshSettings;
            }
            BindCharacter(null);
            if (objectManipulator != null) objectManipulator.OnManipulationBlocked -= ShowManipulationWarning;
            if (IsPaused) ApplyPaused(false);
        }

        private void Update()
        {
            if (flow != null && flow.CurrentScreen == CampaignScreen.Play && character == null)
                BindCharacter(FindAnyObjectByType<WallPlatformerController2D>());
            if (hintPanel != null && hintPanel.activeSelf && Time.unscaledTime >= hideHintAt)
                hintPanel.SetActive(false);
            if (warningPanel != null && warningPanel.activeSelf && Time.unscaledTime >= hideWarningAt)
                warningPanel.SetActive(false);
        }

        private void LateUpdate()
        {
            if (view == null || canvas == null) return;
            Transform cameraTransform = view.transform;
            Vector3 targetPosition = cameraTransform.position + cameraTransform.forward * 1.15f;
            Quaternion targetRotation = Quaternion.LookRotation(cameraTransform.forward, cameraTransform.up);
            bool reducedMotion = progress != null && progress.Settings.reducedMotion;
            float blend = reducedMotion ? 1f : 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            canvas.transform.position = Vector3.Lerp(canvas.transform.position, targetPosition, blend);
            canvas.transform.rotation = Quaternion.Slerp(canvas.transform.rotation, targetRotation, blend);
        }

        public void TogglePause()
        {
            if (flow == null || (flow.CurrentScreen != CampaignScreen.Play && flow.CurrentScreen != CampaignScreen.Build)) return;
            ApplyPaused(!IsPaused);
        }

        public void Resume() => ApplyPaused(false);

        private void ApplyPaused(bool paused)
        {
            IsPaused = paused;
            if (pauseRoot != null) pauseRoot.SetActive(paused);
            if (gameplayRoot != null) gameplayRoot.SetActive(!paused && flow != null && flow.CurrentScreen == CampaignScreen.Play);
            if (scoreHintRoot != null) scoreHintRoot.SetActive(!paused && flow != null &&
                (flow.CurrentScreen == CampaignScreen.Build || flow.CurrentScreen == CampaignScreen.Play));
            if (paused)
            {
                previousTimeScale = Mathf.Approximately(Time.timeScale, 0f) ? 1f : Time.timeScale;
                Time.timeScale = 0f;
                AudioListener.pause = true;
                character?.SetControlsEnabled(false);
                ShowPausePage();
            }
            else
            {
                Time.timeScale = previousTimeScale;
                AudioListener.pause = false;
                character?.SetControlsEnabled(true);
            }
        }

        private void HandleScreenChanged(CampaignScreen screen)
        {
            if (IsPaused) ApplyPaused(false);
            bool playing = screen == CampaignScreen.Play;
            if (gameplayRoot != null) gameplayRoot.SetActive(playing);
            if (scoreHintRoot != null) scoreHintRoot.SetActive(screen == CampaignScreen.Build || playing);
            if (continueRoot != null) continueRoot.SetActive(screen == CampaignScreen.MainMenu && progress != null && progress.HasSave);
            if (completionRoot != null) completionRoot.SetActive(screen == CampaignScreen.LevelComplete);
            if (hintPanel != null) hintPanel.SetActive(false);
            RefreshScoreDisplay();
            if (screen == CampaignScreen.LevelComplete) RefreshCompletionDisplay();
            if (!playing) BindCharacter(null);
        }

        private void HandleRunScoreChanged(int score, int maximum) => RefreshScoreDisplay();

        private void HandleHintRevealed(string message)
        {
            if (hintLabel == null || hintPanel == null) return;
            hintLabel.text = message;
            hintPanel.SetActive(true);
            hideHintAt = Time.unscaledTime + 8f;
            RefreshScoreDisplay();
        }

        private void ShowManipulationWarning(string message)
        {
            if (warningPanel == null || warningLabel == null) return;
            warningLabel.text = string.IsNullOrWhiteSpace(message) ? "This item cannot be moved." : message;
            warningPanel.SetActive(true);
            hideWarningAt = Time.unscaledTime + 2.5f;
        }

        private void RefreshScoreDisplay()
        {
            if (scoreLabel != null)
                scoreLabel.text = flow != null ? $"LEVEL SCORE   {flow.CurrentScore:N0}" : "LEVEL SCORE   0";
            if (hintButton == null || flow == null) return;
            hintButton.interactable = flow.RemainingHints > 0;
            Text label = hintButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                int penalty = flow.SelectedLevel != null ? flow.SelectedLevel.HintPenalty : 0;
                label.text = flow.RemainingHints > 0 ? $"HINT  (−{penalty:N0})" : "NO MORE HINTS";
            }
        }

        private void RefreshCompletionDisplay()
        {
            if (completionLabel == null || flow == null) return;
            completionLabel.text =
                $"LEVEL COMPLETE\n\nSCORE   {flow.LastAwardedScore:N0}\nBEST   {flow.LastBestScore:N0}\nTOTAL   {flow.TotalScore:N0}";
        }

        private void BindCharacter(WallPlatformerController2D target)
        {
            if (character == target) return;
            if (character != null) character.AbilityUsesChanged -= HandleAbilityUsesChanged;
            character = target;
            if (character != null) character.AbilityUsesChanged += HandleAbilityUsesChanged;
            RefreshAbilityLabels();
        }

        private void HandleAbilityUsesChanged(int doubleJumps, int dashes) => RefreshAbilityLabels();

        private void RefreshAbilityLabels()
        {
            int doubleJumps = character != null ? character.DoubleJumpUses : 0;
            int dashes = character != null ? character.DashUses : 0;
            if (doubleJumpLabel != null)
            {
                doubleJumpLabel.text = $"DOUBLE JUMP   × {doubleJumps}";
                doubleJumpLabel.color = doubleJumps > 0 ? Cyan : new Color(0.45f, 0.5f, 0.6f, 1f);
            }
            if (dashLabel != null)
            {
                dashLabel.text = $"DASH   × {dashes}";
                dashLabel.color = dashes > 0 ? Pink : new Color(0.45f, 0.5f, 0.6f, 1f);
            }
        }

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject("Gameplay HUD & Pause Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = view;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 1f;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(Width, Height);

            gameplayRoot = CreateRect("Power-up HUD", canvasObject.transform, Vector2.zero, new Vector2(Width, Height), Color.clear).gameObject;
            Image abilityPanel = CreateRect("Ability Charges", gameplayRoot.transform, new Vector2(365f, 275f), new Vector2(430f, 112f), new Color(0.025f, 0.04f, 0.07f, 0.86f));
            doubleJumpLabel = CreateText("Double Jump Uses", abilityPanel.transform, new Vector2(0f, 25f), new Vector2(390f, 42f), "DOUBLE JUMP   × 0", 25, TextAnchor.MiddleLeft);
            dashLabel = CreateText("Dash Uses", abilityPanel.transform, new Vector2(0f, -24f), new Vector2(390f, 42f), "DASH   × 0", 25, TextAnchor.MiddleLeft);
            scoreHintRoot = CreateRect("Score & Hints", canvasObject.transform, Vector2.zero, new Vector2(Width, Height), Color.clear).gameObject;
            Image scorePanel = CreateRect("Score", scoreHintRoot.transform, new Vector2(0f, 300f), new Vector2(330f, 58f), new Color(0.025f, 0.04f, 0.07f, 0.86f));
            scoreLabel = CreateText("Current Score", scorePanel.transform, Vector2.zero, new Vector2(300f, 44f), "LEVEL SCORE   0", 24, TextAnchor.MiddleCenter);
            CreateButton("Jump Button", gameplayRoot.transform, new Vector2(390f, -265f), new Vector2(170f, 74f), "JUMP", Cyan, () => character?.RequestJump());
            CreateButton("Dash Button", gameplayRoot.transform, new Vector2(205f, -265f), new Vector2(170f, 74f), "DASH", Pink, () => character?.RequestDash());
            hintButton = CreateButton("Hint Button", scoreHintRoot.transform, new Vector2(-330f, -265f), new Vector2(250f, 74f), "HINT", new Color(1f, 0.72f, 0.24f), RequestHint);
            CreateButton("Pause Button", gameplayRoot.transform, new Vector2(-500f, 300f), new Vector2(150f, 54f), "PAUSE", new Color(0.5f, 0.58f, 0.7f), TogglePause);

            Image hintBackground = CreateRect("Hint Panel", scoreHintRoot.transform, new Vector2(0f, 120f), new Vector2(720f, 170f), PanelColor);
            hintPanel = hintBackground.gameObject;
            hintBackground.raycastTarget = false;
            hintLabel = CreateText("Hint Text", hintPanel.transform, Vector2.zero, new Vector2(660f, 140f), string.Empty, 25, TextAnchor.MiddleCenter);
            hintPanel.SetActive(false);

            continueRoot = CreateRect("Continue Save Overlay", canvasObject.transform, Vector2.zero, new Vector2(Width, Height), Color.clear).gameObject;
            CreateButton("Continue Local Save", continueRoot.transform, new Vector2(0f, -292f), new Vector2(360f, 58f), "CONTINUE LOCAL SAVE", new Color(0.4f, 0.72f, 1f), () => flow?.ContinueFromLocalSave());

            completionRoot = CreateRect("Score Result Overlay", canvasObject.transform, Vector2.zero, new Vector2(Width, Height), Color.clear).gameObject;
            Image completionPanel = CreateRect("Score Result", completionRoot.transform, new Vector2(300f, 50f), new Vector2(430f, 290f), PanelColor);
            completionPanel.raycastTarget = false;
            completionLabel = CreateText("Score Result Text", completionPanel.transform, Vector2.zero, new Vector2(390f, 250f), string.Empty, 28, TextAnchor.MiddleCenter);
            completionRoot.SetActive(false);

            Image warningBackground = CreateRect("Locked Item Warning", canvasObject.transform, new Vector2(0f, -190f), new Vector2(650f, 82f), new Color(0.55f, 0.12f, 0.12f, 0.96f));
            warningPanel = warningBackground.gameObject;
            warningBackground.raycastTarget = false;
            warningLabel = CreateText("Warning Text", warningPanel.transform, Vector2.zero, new Vector2(610f, 62f), string.Empty, 24, TextAnchor.MiddleCenter);
            warningPanel.SetActive(false);

            pauseRoot = CreateRect("Pause Overlay", canvasObject.transform, Vector2.zero, new Vector2(Width, Height), new Color(0f, 0f, 0f, 0.62f)).gameObject;
            Image menuPanel = CreateRect("Pause Panel", pauseRoot.transform, Vector2.zero, new Vector2(620f, 610f), PanelColor);
            CreateText("Pause Title", menuPanel.transform, new Vector2(0f, 248f), new Vector2(540f, 70f), "SHADOW CAST", 42, TextAnchor.MiddleCenter).color = Color.white;

            pausePage = CreateRect("Pause Page", menuPanel.transform, Vector2.zero, new Vector2(600f, 500f), Color.clear).gameObject;
            CreateText("Paused Label", pausePage.transform, new Vector2(0f, 174f), new Vector2(520f, 50f), "PAUSED", 30, TextAnchor.MiddleCenter).color = Cyan;
            CreateButton("Resume", pausePage.transform, new Vector2(0f, 105f), new Vector2(420f, 58f), "RESUME", Cyan, Resume);
            CreateButton("Save", pausePage.transform, new Vector2(0f, 35f), new Vector2(420f, 58f), "SAVE GAME", new Color(0.25f, 0.75f, 0.55f), SaveGame);
            CreateButton("Load", pausePage.transform, new Vector2(0f, -35f), new Vector2(420f, 58f), "LOAD LAST SAVE", new Color(0.4f, 0.62f, 1f), LoadGame);
            CreateButton("Settings", pausePage.transform, new Vector2(0f, -105f), new Vector2(420f, 58f), "SETTINGS", Pink, ShowSettingsPage);
            CreateButton("Main Menu", pausePage.transform, new Vector2(0f, -175f), new Vector2(420f, 58f), "RETURN TO MAIN MENU", new Color(0.85f, 0.42f, 0.32f), ReturnToMainMenu);

            settingsPage = CreateRect("Settings Page", menuPanel.transform, Vector2.zero, new Vector2(600f, 500f), Color.clear).gameObject;
            CreateText("Settings Title", settingsPage.transform, new Vector2(0f, 174f), new Vector2(520f, 50f), "SETTINGS", 30, TextAnchor.MiddleCenter).color = Pink;
            settingsSummary = CreateText("Settings Summary", settingsPage.transform, new Vector2(0f, 85f), new Vector2(500f, 90f), string.Empty, 24, TextAnchor.MiddleCenter);
            CreateButton("Volume Down", settingsPage.transform, new Vector2(-115f, 10f), new Vector2(190f, 58f), "VOLUME −", Cyan, () => ChangeVolume(-0.1f));
            CreateButton("Volume Up", settingsPage.transform, new Vector2(115f, 10f), new Vector2(190f, 58f), "VOLUME +", Cyan, () => ChangeVolume(0.1f));
            CreateButton("Reduced Motion", settingsPage.transform, new Vector2(0f, -65f), new Vector2(420f, 58f), "TOGGLE REDUCED MOTION", Pink, ToggleReducedMotion);
            CreateButton("Settings Back", settingsPage.transform, new Vector2(0f, -155f), new Vector2(420f, 58f), "BACK", new Color(0.5f, 0.58f, 0.7f), ShowPausePage);

            statusLabel = CreateText("Save Status", menuPanel.transform, new Vector2(0f, -260f), new Vector2(520f, 36f), string.Empty, 18, TextAnchor.MiddleCenter);
            pauseRoot.SetActive(false);
            settingsPage.SetActive(false);
        }

        private void RequestHint()
        {
            if (flow == null || flow.UseHint()) return;
            if (hintLabel != null && hintPanel != null)
            {
                hintLabel.text = "No more hints are available for this level.";
                hintPanel.SetActive(true);
                hideHintAt = Time.unscaledTime + 4f;
            }
        }

        private void ShowPausePage()
        {
            pausePage.SetActive(true);
            settingsPage.SetActive(false);
            RefreshSaveStatus();
        }

        private void ShowSettingsPage()
        {
            pausePage.SetActive(false);
            settingsPage.SetActive(true);
            RefreshSettings();
        }

        private void SaveGame()
        {
            bool saved = flow != null && flow.SaveLocalProgress();
            statusLabel.text = saved ? "Saved locally." : "Save failed. Check the Console.";
            statusLabel.color = saved ? Cyan : new Color(1f, 0.35f, 0.35f);
        }

        private void LoadGame()
        {
            bool loaded = flow != null && flow.ContinueFromLocalSave();
            statusLabel.text = loaded ? "Save loaded." : "No compatible local save found.";
            if (loaded) ApplyPaused(false);
        }

        private void ReturnToMainMenu()
        {
            ApplyPaused(false);
            flow?.ReturnToMainMenu();
        }

        private void ChangeVolume(float delta)
        {
            if (progress == null) return;
            progress.SetMasterVolume(progress.Settings.masterVolume + delta);
            RefreshSettings();
        }

        private void ToggleReducedMotion()
        {
            if (progress == null) return;
            progress.SetReducedMotion(!progress.Settings.reducedMotion);
            RefreshSettings();
        }

        private void RefreshSettings()
        {
            if (settingsSummary == null || progress == null) return;
            settingsSummary.text = $"MASTER VOLUME   {Mathf.RoundToInt(progress.Settings.masterVolume * 100f)}%\nREDUCED MOTION   {(progress.Settings.reducedMotion ? "ON" : "OFF")}";
        }

        private void RefreshSaveStatus()
        {
            if (statusLabel == null || progress == null) return;
            statusLabel.color = new Color(0.7f, 0.76f, 0.86f);
            statusLabel.text = progress.LastSavedAtUtc.HasValue
                ? $"Last save: {progress.LastSavedAtUtc.Value.ToLocalTime():g}"
                : "Progress auto-saves when levels are completed.";
        }

        private void HandleSaved()
        {
            RefreshSaveStatus();
            RefreshSettings();
        }

        private static Image CreateRect(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            GameObject target = new GameObject(name, typeof(RectTransform), typeof(Image));
            target.transform.SetParent(parent, false);
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = target.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0.01f;
            return image;
        }

        private static Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, string content, int fontSize, TextAnchor alignment)
        {
            GameObject target = new GameObject(name, typeof(RectTransform), typeof(Text));
            target.transform.SetParent(parent, false);
            RectTransform rect = target.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = target.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, Vector2 position, Vector2 size, string label, Color accent, Action action)
        {
            Image image = CreateRect(name, parent, position, size, new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 0.96f));
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            button.colors = colors;
            Text text = CreateText("Label", image.transform, Vector2.zero, size - new Vector2(20f, 8f), label, 22, TextAnchor.MiddleCenter);
            text.color = Color.white;
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }
    }
}
