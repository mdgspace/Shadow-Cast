using System;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Data binder for an exported Figma chapter/level card prefab.</summary>
    public sealed class CampaignSelectionCard : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Text title;
        [SerializeField] private Image artwork;
        [SerializeField] private GameObject lockedIndicator;
        [SerializeField] private GameObject completedIndicator;

        private Action selected;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
        }

        public void Bind(string label, Sprite image, bool locked, bool completed, Action onSelected)
        {
            if (title != null) title.text = label;
            if (artwork != null)
            {
                artwork.sprite = image;
                artwork.enabled = image != null;
            }
            if (lockedIndicator != null) lockedIndicator.SetActive(locked);
            if (completedIndicator != null) completedIndicator.SetActive(completed);
            if (button != null) button.interactable = !locked;
            if (button != null && selected != null) button.onClick.RemoveListener(InvokeSelected);
            selected = onSelected;
            if (button != null) button.onClick.AddListener(InvokeSelected);
        }

        private void InvokeSelected() => selected?.Invoke();
    }
}
