using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The countdown that appears on exactly one player's screen.
    ///
    /// It is a bare number with no label and no explanation, which is deliberate. A first-time player
    /// has no idea what it counts down to, and that is the most frightening it will ever be. By the
    /// second or third run everyone knows, and it becomes something else entirely: the cue to start
    /// reading out every code and location you are holding before you lose the ability to.
    ///
    /// Nobody else sees anything. If you want the group to know you are marked, you have to say so.
    /// </summary>
    public class DoomTimerUI : MonoBehaviour
    {
        public static DoomTimerUI Instance { get; private set; }

        private CanvasGroup group;
        private TextMeshProUGUI label;
        private Image vignette;
        private float remaining;
        private bool running;

        private void Awake()
        {
            Instance = this;

            var canvasGO = new GameObject("DoomTimerCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70;
            MenuTheme.ScaleCanvas(canvasGO);
            group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var vgo = new GameObject("Vignette", typeof(RectTransform));
            vgo.transform.SetParent(canvasGO.transform, false);
            vignette = vgo.AddComponent<Image>();
            vignette.color = new Color(0.35f, 0f, 0f, 0f);
            vignette.raycastTarget = false;
            var vr = (RectTransform)vgo.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = Vector2.zero; vr.offsetMax = Vector2.zero;

            var tgo = new GameObject("Count", typeof(RectTransform));
            tgo.transform.SetParent(canvasGO.transform, false);
            label = tgo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 46f;
            label.color = new Color(0.86f, 0.13f, 0.10f);
            label.raycastTarget = false;
            var tr = (RectTransform)tgo.transform;
            tr.sizeDelta = new Vector2(320f, 70f);
            tr.anchoredPosition = new Vector2(0f, 190f);
        }

        public void Begin(float seconds)
        {
            remaining = seconds;
            running = true;
        }

        public void Clear()
        {
            running = false;
            if (group != null) group.alpha = 0f;
        }

        private void Update()
        {
            if (!running || group == null) return;
            remaining -= Time.deltaTime;
            if (remaining <= 0f) { Clear(); return; }

            group.alpha = 1f;
            label.text = Mathf.CeilToInt(remaining).ToString();

            // Tightens as it runs out: the number pulses faster and the edges bleed in. Under ten
            // seconds there is no point talking any more, and the UI should say so before you work
            // it out.
            float urgency = Mathf.Clamp01(1f - remaining / 30f);
            float pulse = 1f + Mathf.Sin(Time.time * (3f + urgency * 12f)) * 0.06f * (0.3f + urgency);
            label.rectTransform.localScale = Vector3.one * pulse;
            vignette.color = new Color(0.35f, 0f, 0f, urgency * 0.28f);
        }
    }
}
