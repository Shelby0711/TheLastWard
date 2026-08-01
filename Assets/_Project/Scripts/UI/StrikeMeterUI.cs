using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The little ring that fills while you are knelt over a candle.
    ///
    /// Three seconds of holding still with no feedback is indistinguishable from three seconds of the
    /// game having ignored you — and since any input cancels the strike and burns the match, a player
    /// who does not know the action started will move, lose a match, and never learn why. The ring is
    /// what makes the flinch rule legible.
    ///
    /// Self-building, like the other HUD pieces here, so nothing in the level has to wire it up.
    /// </summary>
    public class StrikeMeterUI : MonoBehaviour
    {
        public static StrikeMeterUI Instance { get; private set; }

        private Image ring;
        private CanvasGroup group;

        private void Awake()
        {
            Instance = this;

            var canvasGO = new GameObject("StrikeMeterCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            MenuTheme.ScaleCanvas(canvasGO);
            group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            // Just below the crosshair, so it sits where you are already looking.
            var back = NewImage(canvasGO.transform, "Ring_BG", new Color(0f, 0f, 0f, 0.55f));
            back.rectTransform.sizeDelta = new Vector2(54f, 54f);
            back.rectTransform.anchoredPosition = new Vector2(0f, -58f);
            back.sprite = RingSprite();
            back.type = Image.Type.Simple;

            ring = NewImage(canvasGO.transform, "Ring_Fill", new Color(1f, 0.62f, 0.24f, 0.95f));
            ring.rectTransform.sizeDelta = new Vector2(54f, 54f);
            ring.rectTransform.anchoredPosition = new Vector2(0f, -58f);
            ring.sprite = RingSprite();
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
        }

        private static Image NewImage(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        // A ring drawn once at runtime, so this needs no art asset and cannot go missing on rebuild.
        private static Sprite cached;
        private static Sprite RingSprite()
        {
            if (cached != null) return cached;
            const int S = 96;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float outer = S * 0.5f, inner = S * 0.5f - 6f, c = S * 0.5f - 0.5f;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(Mathf.Min(outer - d, d - inner));
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            cached = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            return cached;
        }

        /// <summary>0..1, or negative to hide it.</summary>
        public void SetProgress(float t)
        {
            if (group == null) return;
            bool show = t >= 0f;
            group.alpha = show ? 1f : 0f;
            if (show && ring != null) ring.fillAmount = Mathf.Clamp01(t);
        }

        public void Hide() => SetProgress(-1f);
    }
}
