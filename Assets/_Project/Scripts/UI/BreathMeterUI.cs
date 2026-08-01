using LastWard.Player;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The breath bar. Only on screen while you are actually holding — the rest of the time it would
    /// just be another permanent gauge, and this game has enough of those.
    ///
    /// It drains right to left and runs <b>green to amber to red</b> as the air goes, so the state you
    /// need at a glance ("can I still wait, or do I run now?") is readable by colour alone without
    /// looking at it directly. Red arrives with roughly a third left, which is about the time it takes
    /// to get somewhere else — the warning is early enough to act on rather than just a countdown to
    /// something you cannot avoid.
    ///
    /// Builds its own canvas so it needs no scene wiring: drop the component on any object.
    /// </summary>
    public class BreathMeterUI : MonoBehaviour
    {
        [SerializeField] private Vector2 size = new Vector2(220f, 10f);
        [SerializeField] private float bottomMargin = 96f;
        [SerializeField] private float fadeSpeed = 8f;

        private static readonly Color Full = new Color(0.35f, 0.85f, 0.35f);
        private static readonly Color Mid = new Color(0.95f, 0.80f, 0.25f);
        private static readonly Color Low = new Color(0.90f, 0.18f, 0.15f);

        private CanvasGroup group;
        private Image fill;
        private Text label;

        private void Start()
        {
            var canvasGO = new GameObject("BreathMeterCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            MenuTheme.ScaleCanvas(canvasGO);
            group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var back = MakePanel(canvasGO.transform, size, new Color(0f, 0f, 0f, 0.55f));
            back.anchorMin = back.anchorMax = new Vector2(0.5f, 0f);
            back.pivot = new Vector2(0.5f, 0f);
            back.anchoredPosition = new Vector2(0f, bottomMargin);

            var fillRT = MakePanel(back, new Vector2(size.x - 4f, size.y - 4f), Full);
            fill = fillRT.GetComponent<Image>();
            // Left-anchored so shrinking the width drains it from the right, like air running out.
            fillRT.anchorMin = new Vector2(0f, 0.5f);
            fillRT.anchorMax = new Vector2(0f, 0.5f);
            fillRT.pivot = new Vector2(0f, 0.5f);
            fillRT.anchoredPosition = new Vector2(2f, 0f);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(back, false);
            label = labelGO.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 1f, 1f, 0.75f);
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.5f, 1f);
            lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.sizeDelta = new Vector2(240f, 18f);
            lrt.anchoredPosition = new Vector2(0f, 3f);
        }

        private RectTransform MakePanel(Transform parent, Vector2 sz, Color colour)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = colour;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = sz;
            return rt;
        }

        private void Update()
        {
            if (group == null) return;

            var hold = PlayerBreathHold.Local;
            // Shown while held, and for the moment after, so you see how little you got back.
            bool show = hold != null && (hold.IsHolding || hold.Breath < 0.999f);
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.deltaTime * fadeSpeed);
            if (hold == null || group.alpha <= 0.001f) return;

            float t = Mathf.Clamp01(hold.Breath);
            var rt = fill.rectTransform;
            rt.sizeDelta = new Vector2((size.x - 4f) * t, size.y - 4f);
            // Green while there is room to think, amber as it gets real, red while you are running out.
            fill.color = t > 0.55f ? Color.Lerp(Mid, Full, (t - 0.55f) / 0.45f)
                                   : Color.Lerp(Low, Mid, t / 0.55f);

            label.text = hold.IsHolding
                ? $"HOLDING BREATH   {hold.SecondsLeft:0.0}s"
                : "catching your breath";
        }
    }
}
