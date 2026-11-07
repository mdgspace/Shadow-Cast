using System;
using System.Collections.Generic;
using UnityEngine;
using ShadowCast.Core.BuildMode;

namespace ShadowCast.Core.Campaign
{
    public enum CampaignScreen
    {
        MainMenu,
        ChapterSelect,
        LevelSelect,
        WallPlacement,
        Build,
        Baking,
        Play,
        LevelComplete,
        LevelFailed
    }

    /// <summary>Owns campaign navigation and connects menus, level loading, baking, play, and progress.</summary>
    public sealed class CampaignFlowController : MonoBehaviour
    {
        [SerializeField] private List<ChapterDefinition> chapters = new List<ChapterDefinition>();
        [SerializeField] private CampaignProgressStore progressStore;
        [SerializeField] private CampaignLevelRuntime levelRuntime;
        [SerializeField] private BuildModeManager buildModeManager;

        private ChapterDefinition selectedChapter;
        private LevelDefinition selectedLevel;
        private int selectedChapterIndex = -1;
        private int selectedLevelIndex = -1;
        private Pose confirmedWallPose;
        private CampaignScreen currentScreen = CampaignScreen.MainMenu;
        private bool buildEventsRegistered;

        private void OnValidate()
        {
            HashSet<string> chapterIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> levelIds = new HashSet<string>(StringComparer.Ordinal);
            for (int chapterIndex = 0; chapterIndex < chapters.Count; chapterIndex++)
            {
                ChapterDefinition chapter = chapters[chapterIndex];
                if (chapter == null) continue;
                if (!string.IsNullOrWhiteSpace(chapter.Id) && !chapterIds.Add(chapter.Id))
                    Debug.LogWarning($"Campaign contains duplicate chapter ID '{chapter.Id}'. Chapter IDs should be unique.", this);
                for (int levelIndex = 0; levelIndex < chapter.Levels.Count; levelIndex++)
                {
                    LevelDefinition level = chapter.Levels[levelIndex];
                    if (level == null) continue;
                    if (!string.IsNullOrWhiteSpace(level.Id) && !levelIds.Add(level.Id))
                        Debug.LogWarning($"Campaign contains duplicate level ID '{level.Id}'. Level IDs should be unique for local progress.", this);
                }
            }
        }

        public event Action<CampaignScreen> ScreenChanged;
        public event Action<string> MessageChanged;
        public event Action ProgressChanged;

        public IReadOnlyList<ChapterDefinition> Chapters => chapters;
        public CampaignProgressStore Progress => progressStore;
        public CampaignScreen CurrentScreen => currentScreen;
        public ChapterDefinition SelectedChapter => selectedChapter;
        public LevelDefinition SelectedLevel => selectedLevel;
        public int SelectedChapterIndex => selectedChapterIndex;
        public int SelectedLevelIndex => selectedLevelIndex;

        private void Awake()
        {
            if (progressStore == null) progressStore = GetComponent<CampaignProgressStore>();
            if (progressStore == null) progressStore = gameObject.AddComponent<CampaignProgressStore>();
            if (levelRuntime == null) levelRuntime = GetComponent<CampaignLevelRuntime>();
            if (levelRuntime == null) levelRuntime = GetComponentInChildren<CampaignLevelRuntime>();
            if (buildModeManager == null) buildModeManager = GetComponent<BuildModeManager>();
            if (buildModeManager == null) buildModeManager = FindAnyObjectByType<BuildModeManager>();
        }

        private void OnEnable()
        {
            RegisterBuildEvents();
        }

        private void OnDisable()
        {
            UnregisterBuildEvents();
        }

        private void Start()
        {
            // Awake may precede BuildModeManager's required-component setup on another object.
            if (buildModeManager == null) buildModeManager = FindAnyObjectByType<BuildModeManager>();
            RegisterBuildEvents();
            SetScreen(CampaignScreen.MainMenu);
        }

        public void OpenChapterSelect() => SetScreen(CampaignScreen.ChapterSelect);

        public bool SelectChapter(int chapterIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= chapters.Count || chapters[chapterIndex] == null)
                return false;
            ChapterDefinition chapter = chapters[chapterIndex];
            bool hasUnlockedLevel = false;
            for (int i = 0; i < chapter.Levels.Count; i++)
                hasUnlockedLevel |= progressStore.IsUnlocked(chapters, chapterIndex, i);
            if (!hasUnlockedLevel) return false;

            selectedChapter = chapter;
            selectedChapterIndex = chapterIndex;
            SetScreen(CampaignScreen.LevelSelect);
            return true;
        }

        public bool SelectLevel(int levelIndex)
        {
            if (selectedChapter == null || levelIndex < 0 || levelIndex >= selectedChapter.Levels.Count)
                return false;
            if (!progressStore.IsUnlocked(chapters, selectedChapterIndex, levelIndex)) return false;

            selectedLevel = selectedChapter.Levels[levelIndex];
            if (selectedLevel == null) return false;
            selectedLevelIndex = levelIndex;
            SetScreen(CampaignScreen.WallPlacement);
            return true;
        }

        public void ConfirmWallPlacement(Pose wallPose)
        {
            if (currentScreen != CampaignScreen.WallPlacement || selectedLevel == null) return;
            if (levelRuntime == null || !levelRuntime.LoadLevel(selectedLevel, wallPose))
            {
                MessageChanged?.Invoke("This level could not be loaded. Check its level setup and try again.");
                return;
            }

            confirmedWallPose = wallPose;
            levelRuntime.StartBuild();
            SetScreen(CampaignScreen.Build);
        }

        public void BakeLevel()
        {
            if (currentScreen != CampaignScreen.Build || selectedLevel == null || buildModeManager == null || levelRuntime == null)
                return;
            levelRuntime?.StartBake();
            SetScreen(CampaignScreen.Baking);
            buildModeManager.TriggerBake(levelRuntime != null ? levelRuntime.CharacterStartWorldPosition : (Vector3?)null);
        }

        public void ReturnToBuild()
        {
            if (currentScreen != CampaignScreen.Play || buildModeManager == null) return;
            buildModeManager.UnbakeAndReturnToBuild();
            levelRuntime?.StopPlay();
            SetScreen(CampaignScreen.Build);
        }

        public void CompleteLevel()
        {
            if (currentScreen != CampaignScreen.Play || selectedLevel == null) return;
            selectedLevel.MechanicHooks?.levelCompleted?.Invoke();
            progressStore.MarkCompleted(selectedLevel.Id);
            ProgressChanged?.Invoke();
            SetScreen(CampaignScreen.LevelComplete);
        }

        public void FailLevel(string reason = "Try again from the level start.")
        {
            if (currentScreen != CampaignScreen.Play) return;
            selectedLevel?.MechanicHooks?.levelFailed?.Invoke();
            levelRuntime?.StopPlay();
            MessageChanged?.Invoke(reason);
            SetScreen(CampaignScreen.LevelFailed);
        }

        public void RetryLevel()
        {
            if (selectedLevel == null || selectedChapter == null || levelRuntime == null) return;
            buildModeManager?.UnbakeAndReturnToBuild();
            if (!levelRuntime.LoadLevel(selectedLevel, confirmedWallPose))
            {
                MessageChanged?.Invoke("This level could not be reset. Return to level select and try again.");
                return;
            }
            levelRuntime.StartBuild();
            SetScreen(CampaignScreen.Build);
        }

        public void ContinueAfterCompletion()
        {
            if (selectedChapter == null) return;
            int nextChapter = selectedChapterIndex;
            int nextLevel = selectedLevelIndex + 1;
            if (nextLevel >= selectedChapter.Levels.Count)
            {
                nextChapter++;
                nextLevel = 0;
            }

            if (nextChapter < chapters.Count && progressStore.IsUnlocked(chapters, nextChapter, nextLevel))
            {
                SelectChapter(nextChapter);
                SelectLevel(nextLevel);
            }
            else
            {
                SetScreen(CampaignScreen.LevelSelect);
            }
        }

        public void BackToLevelSelect()
        {
            if (levelRuntime != null) levelRuntime.ClearLevel();
            SetScreen(CampaignScreen.LevelSelect);
        }

        public void BackToChapterSelect()
        {
            if (levelRuntime != null) levelRuntime.ClearLevel();
            selectedChapter = null;
            selectedLevel = null;
            selectedChapterIndex = selectedLevelIndex = -1;
            SetScreen(CampaignScreen.ChapterSelect);
        }

        public void ReturnToMainMenu()
        {
            if (levelRuntime != null) levelRuntime.ClearLevel();
            selectedChapter = null;
            selectedLevel = null;
            selectedChapterIndex = selectedLevelIndex = -1;
            SetScreen(CampaignScreen.MainMenu);
        }

        public bool IsLevelUnlocked(int chapterIndex, int levelIndex) => progressStore != null && progressStore.IsUnlocked(chapters, chapterIndex, levelIndex);
        public bool IsLevelCompleted(int chapterIndex, int levelIndex)
        {
            if (chapterIndex < 0 || chapterIndex >= chapters.Count || chapters[chapterIndex] == null) return false;
            if (levelIndex < 0 || levelIndex >= chapters[chapterIndex].Levels.Count) return false;
            LevelDefinition level = chapters[chapterIndex].Levels[levelIndex];
            return level != null && progressStore != null && progressStore.IsCompleted(level.Id);
        }

        private void HandleBakeStateChanged(BakeState state)
        {
            if (state == BakeState.Baking) SetScreen(CampaignScreen.Baking);
        }

        private void HandleBakeCompleted(Vector3 spawnPosition)
        {
            if (currentScreen != CampaignScreen.Baking) return;
            levelRuntime?.StartPlay(spawnPosition);
            SetScreen(CampaignScreen.Play);
        }

        private void HandleBakeFailed(string message)
        {
            MessageChanged?.Invoke(message);
            SetScreen(CampaignScreen.Build);
        }

        private void SetScreen(CampaignScreen screen)
        {
            currentScreen = screen;
            if (buildModeManager != null && buildModeManager.InputBridge != null)
                buildModeManager.InputBridge.enabled = screen == CampaignScreen.Build;
            ScreenChanged?.Invoke(screen);
        }

        private void RegisterBuildEvents()
        {
            if (buildEventsRegistered || buildModeManager == null || buildModeManager.BakePipeline == null) return;
            buildModeManager.OnBakeCompleted += HandleBakeCompleted;
            buildModeManager.BakePipeline.OnBakeStateChanged += HandleBakeStateChanged;
            buildModeManager.BakePipeline.OnBakeFailed += HandleBakeFailed;
            buildEventsRegistered = true;
        }

        private void UnregisterBuildEvents()
        {
            if (!buildEventsRegistered || buildModeManager == null || buildModeManager.BakePipeline == null) return;
            buildModeManager.OnBakeCompleted -= HandleBakeCompleted;
            buildModeManager.BakePipeline.OnBakeStateChanged -= HandleBakeStateChanged;
            buildModeManager.BakePipeline.OnBakeFailed -= HandleBakeFailed;
            buildEventsRegistered = false;
        }
    }
}
