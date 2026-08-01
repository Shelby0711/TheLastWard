using LastWard.Player;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// Five bars in the top-right corner showing what is left in the torch.
    ///
    /// Discrete bars rather than a continuous gauge on purpose: "three bars left" is something you can
    /// hold in your head while running, and it makes spending the light feel like spending a countable
    /// resource. The partial bar drains within itself so the current minute is still readable.
    ///
    /// Builds its own canvas, so it needs no scene wiring.
    /// </summary>
    public class BatteryMeterUI : MonoBehaviour
    {
        [SerializeField] private Vector2 barSize = new Vector2(16f, 7f);
        [SerializeField] private float gap = 3f;
        [SerializeField] private Vector2 margin = new Vector2(18f, 18f);

        private static readonly Color Good = new Color(0.85f, 0.85f, 0.80f);
        private static readonly Color Low = new Color(0.90f, 0.65f, 0.20f);
        private static readonly Color Critical = new Color(0.90f, 0.20f, 0.15f);
        private static readonly Color Empty = new Color(1f, 1f, 1f, 0.13f);

        private Image[] fills;
        private CanvasGroup group;

        private void Start()
        {
            var canvasGO = new GameObject("BatteryCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            MenuTheme.ScaleCanvas(canvasGO);
            group = canvasGO.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            fills = new Image[FlashlightBattery.Bars];
            for (int i = 0; i < FlashlightBattery.Bars; i++)
            {
                // Laid out right to left, so the bar that empties first is the leftmost — the row
                // shortens toward the corner the way a battery icon does.
                float x = -margin.x - (barSize.x + gap) * (FlashlightBattery.Bars - 1 - i);

                var slot = MakePanel(canvasGO.transform, barSize, Empty);
                slot.anchorMin = slot.anchorMax = new Vector2(1f, 1f);
                slot.pivot = new Vector2(1f, 1f);
                slot.anchoredPosition = new Vector2(x, -margin.y);

                var fill = MakePanel(slot, new Vector2(barSize.x - 2f, barSize.y - 2f), Good);
                fill.anchorMin = new Vector2(0f, 0.5f);
                fill.anchorMax = new Vector2(0f, 0.5f);
                fill.pivot = new Vector2(0f, 0.5f);
                fill.anchoredPosition = new Vector2(1f, 0f);
                fills[i] = fill.GetComponent<Image>();
            }
        }

        private RectTransform MakePanel(Transform parent, Vector2 size, Color colour)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = colour;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            return rt;
        }

        private void Update()
        {
            if (fills == null) return;
            var battery = FlashlightBattery.Local;
            if (battery == null) { group.alpha = 0f; return; }
            group.alpha = 1f;

            float charge = battery.Charge;
            Color tint = charge <= 1f ? Critical : charge <= 2f ? Low : Good;

            for (int i = 0; i < fills.Length; i++)
            {
                // Each bar owns one unit of charge; the one straddling the boundary drains partially.
                float amount = Mathf.Clamp01(charge - i);
                var rt = fills[i].rectTransform;
                rt.sizeDelta = new Vector2((barSize.x - 2f) * amount, barSize.y - 2f);
                fills[i].color = tint;
                fills[i].enabled = amount > 0.001f;
            }
        }
    }
}
