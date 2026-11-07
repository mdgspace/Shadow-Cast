using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    [CreateAssetMenu(menuName = "Shadow Cast/Campaign/Chapter", fileName = "Chapter")]
    public sealed class ChapterDefinition : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite artwork;
        [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Artwork => artwork;
        public IReadOnlyList<LevelDefinition> Levels => levels;

        private void OnValidate()
        {
            id = string.IsNullOrWhiteSpace(id) ? name : id.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        }
    }
}
