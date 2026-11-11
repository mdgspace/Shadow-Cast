using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Shared physical sizing and stereo material for every campaign UI surface.</summary>
    public static class ShadowCastUIStyle
    {
        public const float Distance = 3.2f;
        public const float MenuWidth = 4.4f;
        public const float HudWidth = 3.0f;
        public static readonly Color Accent = new Color(.79f, .68f, 1f);
        private static Material material;
        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    Shader shader = Resources.Load<Shader>("ShadowCastUI");
                    material = new Material(shader != null ? shader : Shader.Find("UI/Default"))
                    { name = "Shadow Cast Stereo UI", enableInstancing = true };
                }
                return material;
            }
        }

        public static void ConfigureCanvas(Canvas canvas, Camera camera, float pixelWidth, float worldWidth)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.overrideSorting = true;
            canvas.transform.localScale = Vector3.one * (worldWidth / pixelWidth);
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            if (canvas.TryGetComponent(out GraphicRaycaster raycaster)) raycaster.ignoreReversedGraphics = false;
        }

        public static Text Text(string name, Transform parent, Vector2 position, Vector2 size, string content, int fontSize, Color? color = null)
        {
            var target = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
            target.transform.SetParent(parent, false);
            var rect = target.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = target.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.material = Material;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            target.GetComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        public static Button Button(string label, Transform parent, Vector2 position, Vector2 size, Action action)
        {
            var target = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            target.transform.SetParent(parent, false);
            var rect = target.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var button = target.GetComponent<Button>();
            var hitArea = target.GetComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.material = Material;
            float iconSize = Mathf.Min(size.y * .72f, 80, size.x * .22f);
            Image icon = ShadowCastFigmaAssets.Image("Arrow", target.transform, new Vector2(-size.x * .5f + iconSize * .65f, 0), Vector2.one * iconSize);
            button.targetGraphic = icon;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            target.AddComponent<ShadowCastUIFeedback>();
            Text caption = Text("Label", target.transform, new Vector2(iconSize * .6f, 0), size - new Vector2(iconSize * 1.6f, 12), label, 28);
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 18;
            caption.resizeTextMaxSize = 28;
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }

        public static Button ArtworkButton(string asset, string label, Transform parent, Vector2 position, Vector2 size, Action action)
        {
            Button button = Button(label, parent, position, size, action);
            var image = button.GetComponent<Image>();
            image.sprite = ShadowCastFigmaAssets.Sprite(asset);
            image.type = Image.Type.Simple;
            image.color = Color.white;
            button.targetGraphic = image;
            button.transform.Find("Label").gameObject.SetActive(false);
            button.transform.Find("Figma Arrow").gameObject.SetActive(false);
            return button;
        }

        public static void ApplyToHierarchy(Transform root)
        {
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.material = Material;
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                var image = button.GetComponent<Image>();
                if (image != null && image.sprite == null && button.targetGraphic == image)
                {
                    image.color = Color.clear;
                    float iconSize = Mathf.Min(image.rectTransform.rect.height * .72f, 80, image.rectTransform.rect.width * .22f);
                    Image icon = ShadowCastFigmaAssets.Image("Arrow", button.transform,
                        new Vector2(-image.rectTransform.rect.width * .5f + iconSize * .65f, 0), Vector2.one * iconSize);
                    button.targetGraphic = icon;
                    Text label = button.GetComponentInChildren<Text>();
                    if (label != null)
                    {
                        label.rectTransform.anchoredPosition = new Vector2(iconSize * .6f, 0);
                        label.rectTransform.sizeDelta -= new Vector2(iconSize * 1.6f, 0);
                        label.resizeTextForBestFit = true;
                        label.resizeTextMinSize = 18;
                        label.resizeTextMaxSize = 28;
                    }
                }
                button.transition = Selectable.Transition.None;
                if (button.GetComponent<ShadowCastUIFeedback>() == null) button.gameObject.AddComponent<ShadowCastUIFeedback>();
            }
        }
    }

    /// <summary>Visible feedback works for mouse, hand rays, near touch, and either controller.</summary>
    public sealed class ShadowCastUIFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private readonly HashSet<int> hovering = new HashSet<int>();
        private readonly HashSet<int> pressing = new HashSet<int>();
        private Image image;
        private Outline outline;
        private Button button;
        public bool IsHovered => hovering.Count > 0;
        public bool IsPressed => pressing.Count > 0;
        private void Awake()
        {
            button = GetComponent<Button>();
            Refresh();
        }
        public void OnPointerEnter(PointerEventData data) { hovering.Add(data.pointerId); Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovering.Remove(data.pointerId); Refresh(); }
        public void OnPointerDown(PointerEventData data) { pressing.Add(data.pointerId); Refresh(); }
        public void OnPointerUp(PointerEventData data) { pressing.Remove(data.pointerId); Refresh(); }
        private void OnDisable() { hovering.Clear(); pressing.Clear(); Refresh(); }
        private void Update() { if (button != null && !button.interactable) Refresh(); }
        private void Refresh()
        {
            Image targetImage = button != null ? button.targetGraphic as Image : null;
            if (targetImage != image)
            {
                if (outline != null) outline.effectColor = Color.clear;
                image = targetImage;
                if (image != null)
                {
                    outline = image.GetComponent<Outline>() ?? image.gameObject.AddComponent<Outline>();
                    outline.effectDistance = new Vector2(2, -2);
                }
            }
            if (image == null) return;
            bool available = button == null || button.interactable;
            // Multiply the original artwork by white at rest; preserve its Figma colors.
            image.color = !available ? new Color(.55f, .55f, .55f, .6f) : IsPressed ? new Color(.70f, .62f, .9f, 1f) :
                IsHovered ? new Color(1.15f, 1.12f, 1.25f, 1f) : Color.white;
            Transform caption = transform.Find("Label");
            if (caption != null && caption.TryGetComponent(out Text label))
                label.color = !available ? new Color(.6f, .6f, .6f) : IsHovered || IsPressed ? ShadowCastUIStyle.Accent : Color.white;
            if (outline != null) outline.effectColor = !available ? new Color(.25f, .3f, .35f, .4f) :
                IsHovered || IsPressed ? ShadowCastUIStyle.Accent : Color.clear;
        }
    }

    public sealed class ShadowCastUIHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Action<bool> Changed;
        private readonly HashSet<int> pointers = new HashSet<int>();
        public void OnPointerDown(PointerEventData data) { if (pointers.Add(data.pointerId) && pointers.Count == 1) Changed?.Invoke(true); }
        public void OnPointerUp(PointerEventData data) { if (pointers.Remove(data.pointerId) && pointers.Count == 0) Changed?.Invoke(false); }
        private void OnDisable() { if (pointers.Count > 0) Changed?.Invoke(false); pointers.Clear(); }
    }
}
