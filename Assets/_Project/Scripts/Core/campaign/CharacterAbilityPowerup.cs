using System;
using UnityEngine;
using UnityEngine.Events;

namespace ShadowCast.Core.Campaign
{
    public enum CharacterAbilityType { DoubleJump, Dash }

    /// <summary>Dedicated collectible that grants a designer-selected number of consumable ability uses.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class CharacterAbilityPowerup : MonoBehaviour, ICampaignInteractable
    {
        [SerializeField] private CharacterAbilityType ability = CharacterAbilityType.DoubleJump;
        [SerializeField, Min(1)] private int usesGranted = 1;
        [SerializeField] private bool hideWhenCollected = true;
        [SerializeField] private UnityEvent collected;

        public event Action Activated;
        public CharacterAbilityType Ability => ability;
        public int UsesGranted => Mathf.Max(1, usesGranted);
        public bool IsCollected { get; private set; }

        private void Reset()
        {
            Collider2D trigger = GetComponent<Collider2D>();
            if (trigger != null) trigger.isTrigger = true;
        }

        private void Awake()
        {
            Collider2D trigger = GetComponent<Collider2D>();
            if (trigger != null) trigger.isTrigger = true;
        }

        private void OnValidate() => usesGranted = Mathf.Max(1, usesGranted);

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (IsCollected) return;
            WallPlatformerController2D character = other.GetComponentInParent<WallPlatformerController2D>();
            if (character == null) return;

            if (ability == CharacterAbilityType.DoubleJump) character.GrantDoubleJumpUses(UsesGranted);
            else character.GrantDashUses(UsesGranted);

            IsCollected = true;
            collected?.Invoke();
            Activated?.Invoke();
            if (hideWhenCollected) gameObject.SetActive(false);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            Collider2D trigger = GetComponent<Collider2D>();
            if (trigger != null) trigger.enabled = enabled && !IsCollected;
        }
    }
}
