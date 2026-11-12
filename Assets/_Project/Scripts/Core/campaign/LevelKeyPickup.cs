using System;
using UnityEngine;
using UnityEngine.Events;

namespace ShadowCast.Core.Campaign
{
    /// <summary>A dedicated collectible. Keys deliberately do not use BuildableObject.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class LevelKeyPickup : MonoBehaviour, ICampaignInteractable
    {
        [SerializeField] private string keyId = "main-key";
        [SerializeField] private bool hideWhenCollected = true;
        [SerializeField] private UnityEvent collected;

        public event Action Activated;
        public string KeyId => string.IsNullOrWhiteSpace(keyId) ? name : keyId.Trim();
        public bool IsCollected { get; private set; }

        private void Reset()
        {
            Collider2D keyCollider = GetComponent<Collider2D>();
            if (keyCollider != null) keyCollider.isTrigger = true;
        }

        private void Awake()
        {
            Collider2D keyCollider = GetComponent<Collider2D>();
            if (keyCollider != null) keyCollider.isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (!IsCollected && other.GetComponentInParent<WallPlatformerController2D>() != null)
                Collect();
        }

        [ContextMenu("Collect Key")]
        public void Collect()
        {
            if (IsCollected) return;
            IsCollected = true;
            collected?.Invoke();
            Activated?.Invoke();
            if (hideWhenCollected) gameObject.SetActive(false);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            Collider2D keyCollider = GetComponent<Collider2D>();
            if (keyCollider != null) keyCollider.enabled = enabled && !IsCollected;
        }
    }
}
