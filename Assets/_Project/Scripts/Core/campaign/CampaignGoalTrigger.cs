using UnityEngine;
using System;

namespace ShadowCast.Core.Campaign
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class CampaignGoalTrigger : MonoBehaviour
    {
        private CampaignFlowController flowController;
        private MonoBehaviour conditionComponent;

        public void Configure(CampaignFlowController flow, MonoBehaviour condition = null)
        {
            flowController = flow;
            conditionComponent = condition;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (flowController == null || other.GetComponentInParent<WallPlatformerController2D>() == null) return;
            if (conditionComponent != null)
            {
                if (!(conditionComponent is ICampaignGoalCondition condition) || !condition.CanCompleteLevel) return;
            }
            flowController.CompleteLevel();
        }
    }

    /// <summary>Future key/lock or objective components can gate the common level goal through this contract.</summary>
    public interface ICampaignGoalCondition
    {
        bool CanCompleteLevel { get; }
    }

    /// <summary>Common lifecycle contract for future level interactables such as keys and hazards.</summary>
    public interface ICampaignInteractable
    {
        event Action Activated;
        void SetInteractionEnabled(bool enabled);
    }
}
