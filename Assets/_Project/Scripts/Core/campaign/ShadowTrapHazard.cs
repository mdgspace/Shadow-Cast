using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Play-mode trigger created for ordinary BuildableObjects marked Is Trap.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ShadowTrapHazard : MonoBehaviour
    {
        [SerializeField] private string failureMessage = "You touched a trap shadow.";

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<WallPlatformerController2D>() == null) return;
            CampaignFlowController flow = FindAnyObjectByType<CampaignFlowController>();
            if (flow != null) flow.FailLevel(failureMessage);
        }
    }
}
