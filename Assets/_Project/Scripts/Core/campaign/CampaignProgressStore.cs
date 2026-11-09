using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    [Serializable]
    public sealed class ShadowCastPlayerSettings
    {
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        public bool reducedMotion;
    }

    /// <summary>Versioned, atomic local persistence for campaign progress, resume state and player settings.</summary>
    public sealed class CampaignProgressStore : MonoBehaviour
    {
        [Serializable]
        private sealed class SaveData
        {
            public int version = 2;
            public List<string> completedLevelIds = new List<string>();
            public string lastChapterId;
            public string lastLevelId;
            public string savedAtUtc;
            public ShadowCastPlayerSettings settings = new ShadowCastPlayerSettings();
        }

        private SaveData data = new SaveData();
        private string SavePath => Path.Combine(Application.persistentDataPath, "shadow-cast-campaign.json");
        private string BackupPath => SavePath + ".bak";

        public event Action Saved;
        public event Action Loaded;
        public ShadowCastPlayerSettings Settings => data.settings;
        public string LastChapterId => data.lastChapterId;
        public string LastLevelId => data.lastLevelId;
        public string LocalSavePath => SavePath;
        public bool HasSave => File.Exists(SavePath);
        public DateTime? LastSavedAtUtc => DateTime.TryParse(data.savedAtUtc, out DateTime value) ? value : null;

        private void Awake() => LoadFromDisk();

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
            SaveToDisk();
        }

        public void RecordLastSelection(string chapterId, string levelId)
        {
            data.lastChapterId = chapterId ?? string.Empty;
            data.lastLevelId = levelId ?? string.Empty;
            SaveToDisk();
        }

        public void SetMasterVolume(float value)
        {
            EnsureDataIntegrity();
            data.settings.masterVolume = Mathf.Clamp01(value);
            ApplySettings();
            SaveToDisk();
        }

        public void SetReducedMotion(bool enabled)
        {
            EnsureDataIntegrity();
            data.settings.reducedMotion = enabled;
            SaveToDisk();
        }

        public bool LoadFromDisk()
        {
            try
            {
                if (File.Exists(SavePath))
                    data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath)) ?? new SaveData();
                else
                    data = new SaveData();
                EnsureDataIntegrity();
                ApplySettings();
                Loaded?.Invoke();
                return File.Exists(SavePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load local save; starting fresh. {exception.Message}", this);
                data = new SaveData();
                EnsureDataIntegrity();
                ApplySettings();
                return false;
            }
        }

        public bool SaveToDisk()
        {
            try
            {
                EnsureDataIntegrity();
                data.savedAtUtc = DateTime.UtcNow.ToString("O");
                string temporaryPath = SavePath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

                if (File.Exists(SavePath))
                {
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    File.Copy(SavePath, BackupPath, true);
                    File.Delete(SavePath);
                    File.Move(temporaryPath, SavePath);
                }
                else
                {
                    File.Move(temporaryPath, SavePath);
                }

                Saved?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not save campaign progress. {exception.Message}", this);
                return false;
            }
        }

        public void ResetProgress()
        {
            ShadowCastPlayerSettings retainedSettings = data.settings ?? new ShadowCastPlayerSettings();
            data = new SaveData { settings = retainedSettings };
            SaveToDisk();
        }

        public bool DeleteLocalSave()
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                data = new SaveData();
                ApplySettings();
                Loaded?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not delete local save. {exception.Message}", this);
                return false;
            }
        }

        private void EnsureDataIntegrity()
        {
            if (data == null) data = new SaveData();
            if (data.completedLevelIds == null) data.completedLevelIds = new List<string>();
            if (data.settings == null) data.settings = new ShadowCastPlayerSettings();
            data.version = 2;
        }

        private void ApplySettings() => AudioListener.volume = Mathf.Clamp01(data.settings.masterVolume);

        private static bool IsValidIndex(IReadOnlyList<ChapterDefinition> chapters, int chapterIndex, int levelIndex) =>
            chapters != null && chapterIndex >= 0 && chapterIndex < chapters.Count && chapters[chapterIndex] != null &&
            levelIndex >= 0 && levelIndex < chapters[chapterIndex].Levels.Count;
    }
}
