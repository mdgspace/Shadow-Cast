using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Individual, unmodified foreground exports from the supplied Figma design.</summary>
    public static class ShadowCastFigmaAssets
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public static Sprite Sprite(string asset, bool sliced = false)
        {
            string key = asset + (sliced ? ":sliced" : "");
            if (sprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Texture2D texture = Resources.Load<Texture2D>("FigmaElements/" + asset);
            if (texture == null) { Debug.LogError("Missing individual Figma element: " + asset); return null; }
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f,
                100, 0, SpriteMeshType.FullRect, sliced ? Vector4.one * 30 : Vector4.zero);
            sprite.name = "Figma / " + asset;
            sprites[key] = sprite;
            return sprite;
        }

        // Older screen exports also serve as atlases for their individual illustration layers.
        // Only these bounded regions are rendered; the photographed room is never a UI layer.
        public static Sprite Illustration(bool chapter, int index)
        {
            string key = (chapter ? "Chapter" : "Level") + index;
            if (sprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            string screen = chapter ? "Chapter Page" : "Level Page";
            Texture2D atlas = Resources.Load<Texture2D>("FigmaScreens/" + screen);
            Rect region;
            if (chapter)
            {
                float[] x = {300, 782, 1245};
                region = new Rect(x[index % 3], 510, 350, 186);
            }
            else
            {
                float[] x = {585, 955, 1325};
                region = new Rect(x[index % 3], index % 6 < 3 ? 520 : 750, 205, 125);
            }
            float sx = atlas.width / 1920f, sy = atlas.height / 1080f;
            region = new Rect(region.x * sx, (1080 - region.yMax) * sy, region.width * sx, region.height * sy);
            var sprite = UnityEngine.Sprite.Create(atlas, region, Vector2.one * .5f);
            sprite.name = "Figma / " + key + " illustration";
            sprites[key] = sprite;
            return sprite;
        }

        public static Image Image(string asset, Transform parent, Vector2 position, Vector2 size)
        {
            var target = new GameObject("Figma " + asset, typeof(RectTransform), typeof(Image));
            target.transform.SetParent(parent, false);
            var image = target.GetComponent<Image>();
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.sprite = Sprite(asset);
            image.material = ShadowCastUIStyle.Material;
            image.raycastTarget = false;
            return image;
        }
    }
}
