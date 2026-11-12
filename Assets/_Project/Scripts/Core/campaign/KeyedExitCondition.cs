using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Automatically gates an exit until every key assigned to it has been collected.</summary>
    [DisallowMultipleComponent]
    public sealed class KeyedExitCondition : MonoBehaviour, ICampaignGoalCondition
    {
        [SerializeField] private List<LevelKeyPickup> requiredKeys = new List<LevelKeyPickup>();
        [SerializeField] private List<MonoBehaviour> additionalConditions = new List<MonoBehaviour>();
        [SerializeField] private Renderer statusRenderer;
        [SerializeField] private Color lockedColor = new Color(0.92f, 0.2f, 0.15f);
        [SerializeField] private Color unlockedColor = new Color(0.18f, 0.9f, 0.48f);

        public bool CanCompleteLevel
        {
            get
            {
                for (int i = 0; i < requiredKeys.Count; i++)
                    if (requiredKeys[i] != null && !requiredKeys[i].IsCollected) return false;
                for (int i = 0; i < additionalConditions.Count; i++)
                    if (additionalConditions[i] is ICampaignGoalCondition condition && !condition.CanCompleteLevel) return false;
                return true;
            }
        }

        public void Configure(IEnumerable<LevelKeyPickup> keys, MonoBehaviour additionalCondition = null)
        {
            requiredKeys.Clear();
            if (keys != null) requiredKeys.AddRange(keys);
            additionalConditions.Clear();
            if (additionalCondition != null) additionalConditions.Add(additionalCondition);
            for (int i = 0; i < requiredKeys.Count; i++)
                if (requiredKeys[i] != null) requiredKeys[i].Activated += RefreshVisual;
            RefreshVisual();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < requiredKeys.Count; i++)
                if (requiredKeys[i] != null) requiredKeys[i].Activated -= RefreshVisual;
        }

        private void RefreshVisual()
        {
            if (statusRenderer == null) statusRenderer = GetComponentInChildren<Renderer>();
            if (statusRenderer == null) return;
            Material material = statusRenderer.material;
            Color color = CanCompleteLevel ? unlockedColor : lockedColor;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }
    }
}
