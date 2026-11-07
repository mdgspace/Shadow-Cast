using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Local campaign completion/unlock persistence keyed by stable chapter and level IDs.</summary>
    public sealed class CampaignProgressStore : MonoBehaviour
    {
        [Serializable]
        private sealed class SaveData
        {
            public List<string> completedLevelIds = new List<string>();
        }

        private SaveData data = new SaveData();
        private string SavePath => Path.Combine(Application.persistentDataPath, "shadow-cast-campaign.json");

        private void Awake() => Load();

        public bool IsCompleted(string levelId) => !string.IsNullOrEmpty(levelId) && data.completedLevelIds.Contains(levelId);

        public bool IsUnlocked(IReadOnlyList<ChapterDefinition> chapters, int chapterIndex, int levelIndex)
        {
            if (!IsValidIndex(chapters, chapterIndex, levelIndex)) return false;
            if (chapterIndex == 0 && levelIndex == 0) return true;

            int previousChapter = chapterIndex;
            int previousLevel = levelIndex - 1;
            if (previousLevel < 0)
            {
                previousChapter--;
                if (previousChapter < 0 || chapters[previousChapter] == null || chapters[previousChapter].Levels.Count == 0)
                    return false;
                previousLevel = chapters[previousChapter].Levels.Count - 1;
            }

            LevelDefinition previous = chapters[previousChapter].Levels[previousLevel];
            return previous != null && IsCompleted(previous.Id);
        }

        public void MarkCompleted(string levelId)
        {
            if (string.IsNullOrWhiteSpace(levelId) || data.completedLevelIds.Contains(levelId)) return;
            data.completedLevelIds.Add(levelId);
            Save();
        }

        public void ResetProgress()
        {
            data.completedLevelIds.Clear();
            Save();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                    data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) ?? new SaveData();
                if (data.completedLevelIds == null) data.completedLevelIds = new List<string>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load campaign progress; starting fresh. {exception.Message}", this);
                data = new SaveData();
            }
        }

        private void Save()
        {
            try
            {
                string temporaryPath = SavePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Move(temporaryPath, SavePath);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not save campaign progress. {exception.Message}", this);
            }
        }

        private static bool IsValidIndex(IReadOnlyList<ChapterDefinition> chapters, int chapterIndex, int levelIndex) =>
            chapters != null && chapterIndex >= 0 && chapterIndex < chapters.Count && chapters[chapterIndex] != null &&
            levelIndex >= 0 && levelIndex < chapters[chapterIndex].Levels.Count;
    }
}
