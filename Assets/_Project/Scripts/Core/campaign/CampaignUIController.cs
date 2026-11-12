using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowCast.Core.Campaign
{
    /// <summary>
    /// Connects imported Figma artwork/screen prefabs to campaign state. This component creates no UI art.
    /// </summary>
    public sealed class CampaignUIController : MonoBehaviour
    {
        [Serializable]
        private sealed class ScreenRoot
        {
            public CampaignScreen screen;
            public GameObject root;
        }

        [Header("Imported Figma screen prefabs")]
        [SerializeField] private List<ScreenRoot> screenRoots = new List<ScreenRoot>();
        [SerializeField] private CampaignFlowController flow;
        [SerializeField] private WallPlacementController wallPlacement;

        [Header("Imported Figma selection card prefabs")]
        [SerializeField] private CampaignSelectionCard chapterCardPrefab;
        [SerializeField] private Transform chapterCardRoot;
        [SerializeField] private CampaignSelectionCard levelCardPrefab;
        [SerializeField] private Transform levelCardRoot;

        [Header("Buttons on imported screens")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button chapterBackButton;
        [SerializeField] private Button levelBackButton;
        [SerializeField] private Button wallBackButton;
        [SerializeField] private Button confirmWallButton;
        [SerializeField] private Button bakeButton;
        [SerializeField] private Button returnToBuildButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button returnToMenuButton;
        [SerializeField] private Text statusLabel;

        private readonly List<CampaignSelectionCard> chapterCards = new List<CampaignSelectionCard>();
        private readonly List<CampaignSelectionCard> levelCards = new List<CampaignSelectionCard>();

        private void Awake()
        {
            if (flow == null) flow = GetComponent<CampaignFlowController>();
            BindButton(playButton, OnPlay);
            BindButton(chapterBackButton, flow != null ? flow.ReturnToMainMenu : null);
            BindButton(levelBackButton, flow != null ? flow.BackToChapterSelect : null);
            BindButton(wallBackButton, flow != null ? flow.BackToLevelSelect : null);
            BindButton(confirmWallButton, ConfirmWall);
            BindButton(bakeButton, flow != null ? flow.BakeLevel : null);
            BindButton(returnToBuildButton, flow != null ? flow.ReturnToBuild : null);
            BindButton(retryButton, flow != null ? flow.RetryLevel : null);
            BindButton(continueButton, flow != null ? flow.ContinueAfterCompletion : null);
            BindButton(returnToMenuButton, flow != null ? flow.ReturnToMainMenu : null);
        }

        private void OnEnable()
        {
            if (flow == null) flow = GetComponent<CampaignFlowController>();
            if (flow != null)
            {
                flow.ScreenChanged += ShowScreen;
                flow.ProgressChanged += RefreshSelection;
                flow.MessageChanged += ShowMessage;
            }
        }

        private void OnDisable()
        {
            if (flow != null)
            {
                flow.ScreenChanged -= ShowScreen;
                flow.ProgressChanged -= RefreshSelection;
                flow.MessageChanged -= ShowMessage;
            }
        }

        private void Start()
        {
            if (flow == null)
            {
                Debug.LogError("CampaignUIController requires a CampaignFlowController.", this);
                return;
            }
            if (screenRoots.Count == 0 || chapterCardPrefab == null || levelCardPrefab == null)
                Debug.LogWarning("Campaign UI artwork is not fully assigned. Import the Figma export package and bind its screen/card prefabs before play.", this);
            RefreshSelection();
            ShowScreen(flow.CurrentScreen);
        }

        public void RefreshSelection()
        {
            if (flow == null) return;
            RebuildChapterCards();
            RebuildLevelCards();
        }

        private void RebuildChapterCards()
        {
            ClearCards(chapterCards);
            if (chapterCardPrefab == null || chapterCardRoot == null) return;
            for (int i = 0; i < flow.Chapters.Count; i++)
            {
                int chapterIndex = i;
                ChapterDefinition chapter = flow.Chapters[i];
                if (chapter == null) continue;
                bool unlocked = false;
                bool completed = chapter.Levels.Count > 0;
                for (int levelIndex = 0; levelIndex < chapter.Levels.Count; levelIndex++)
                {
                    unlocked |= flow.IsLevelUnlocked(chapterIndex, levelIndex);
                    completed &= flow.IsLevelCompleted(chapterIndex, levelIndex);
                }
                CampaignSelectionCard card = Instantiate(chapterCardPrefab, chapterCardRoot);
                card.Bind(chapter.DisplayName, chapter.Artwork, !unlocked, completed,
                    () => flow.SelectChapter(chapterIndex));
                chapterCards.Add(card);
            }
        }

        private void RebuildLevelCards()
        {
            ClearCards(levelCards);
            if (levelCardPrefab == null || levelCardRoot == null || flow.SelectedChapter == null) return;
            for (int i = 0; i < flow.SelectedChapter.Levels.Count; i++)
            {
                int levelIndex = i;
                LevelDefinition level = flow.SelectedChapter.Levels[i];
                if (level == null) continue;
                CampaignSelectionCard card = Instantiate(levelCardPrefab, levelCardRoot);
                card.Bind(level.DisplayName, level.Artwork,
                    !flow.IsLevelUnlocked(flow.SelectedChapterIndex, levelIndex),
                    flow.IsLevelCompleted(flow.SelectedChapterIndex, levelIndex),
                    () => flow.SelectLevel(levelIndex));
                levelCards.Add(card);
            }
        }

        private void ShowScreen(CampaignScreen screen)
        {
            foreach (ScreenRoot entry in screenRoots)
                if (entry != null && entry.root != null)
                    entry.root.SetActive(entry.screen == screen);
            if (screen == CampaignScreen.ChapterSelect || screen == CampaignScreen.LevelSelect)
                RefreshSelection();
            if (screen == CampaignScreen.WallPlacement && wallPlacement != null && flow.SelectedLevel != null)
                wallPlacement.SetPreviewSize(flow.SelectedLevel.WallWidth, flow.SelectedLevel.WallHeight);
        }

        private void ConfirmWall()
        {
            if (wallPlacement == null || !wallPlacement.Confirm(flow))
                ShowMessage("Point at a room wall to place the level, then confirm.");
        }

        private void ShowMessage(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
        }

        private void OnPlay()
        {
            RefreshSelection();
            flow?.OpenChapterSelect();
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || action == null) return;
            button.onClick.AddListener(action);
        }

        private static void ClearCards(List<CampaignSelectionCard> cards)
        {
            for (int i = 0; i < cards.Count; i++)
                if (cards[i] != null) Destroy(cards[i].gameObject);
            cards.Clear();
        }
    }
}
