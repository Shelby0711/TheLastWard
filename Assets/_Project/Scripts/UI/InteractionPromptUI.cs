using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The prompt that sits under the crosshair when you look at something.
    ///
    /// It was plain centred text with the key inlined — "[E]  Read the note" — which read as debug
    /// output and made the two kinds of prompt indistinguishable. There are genuinely two:
    ///
    ///   <b>An offer.</b> Something you can do right now. Gets a key cap and bright text.
    ///   <b>A refusal.</b> The interactable explaining why not — "Two locks, and no keys". No key
    ///   cap, dimmed, because pressing E will do nothing and the UI should say so first.
    ///
    /// Interactables signal which by whether their prompt carries a "[X]" token; availability comes
    /// from the interactor separately, so a keyed prompt you cannot yet satisfy still dims.
    ///
    /// The plate is sized to its content every time the text changes rather than being a fixed
    /// rectangle — a 500px bar behind the word "Battery" is most of what made it look unfinished.
    /// </summary>
    public class InteractionPromptUI : MonoBehaviour
    {
        public static InteractionPromptUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform plate;
        [SerializeField] private RectTransform keyCap;
        [SerializeField] private TMP_Text keyText;
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private Image plateImage;
        [SerializeField] private Image keyCapImage;

        private const float PadX = 20f;
        private const float CapSize = 34f;
        private const float Gap = 13f;
        private const float PlateH = 48f;
        private const float FadeSpeed = 9f;

        // Leading "[E]" / "[Q]" / "[LMB]" — the key the interactable wants, if it names one.
        private static readonly Regex KeyToken = new Regex(@"^\s*\[([^\]]{1,4})\]\s*", RegexOptions.Compiled);

        private static readonly Color OfferInk = new Color(0.88f, 0.86f, 0.82f);
        private static readonly Color RefuseInk = new Color(0.52f, 0.48f, 0.46f);
        private static readonly Color CapInk = new Color(0.93f, 0.90f, 0.85f);

        private string shown;
        private float targetAlpha;

        private void Awake()
        {
            Instance = this;
            shown = null;
            targetAlpha = 0f;
            if (group != null) group.alpha = 0f;
            if (root != null) root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (group == null) return;
            if (Mathf.Approximately(group.alpha, targetAlpha)) return;

            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, FadeSpeed * Time.deltaTime);
            // Only switch off once it has finished fading out, or the fade never renders.
            if (group.alpha <= 0.001f && root != null) root.SetActive(false);
        }

        public void SetPrompt(string prompt) => SetPrompt(prompt, true);

        /// <param name="available">False when the interactable is refusing — you are missing the
        /// item, it is already open, someone else is holding it.</param>
        public void SetPrompt(string prompt, bool available)
        {
            bool show = !string.IsNullOrEmpty(prompt);
            targetAlpha = show ? 1f : 0f;
            if (!show) return;

            if (root != null && !root.activeSelf) root.SetActive(true);

            // Rebuilding the layout for an unchanged string is pure waste — this is driven from the
            // interactor's Update, so it fires every frame you are looking at anything at all.
            string key = $"{prompt}|{available}";
            if (key == shown) return;
            shown = key;

            string label = prompt;
            string cap = null;
            var m = KeyToken.Match(prompt);
            if (m.Success)
            {
                cap = m.Groups[1].Value.Trim().ToUpperInvariant();
                label = prompt.Substring(m.Length);
            }
            else if (available)
            {
                cap = "E";   // the default verb; only refusals are allowed to show no key at all
            }

            Layout(cap, label, available);
        }

        private void Layout(string cap, string label, bool available)
        {
            promptText.text = label;
            promptText.color = available ? OfferInk : RefuseInk;
            float textW = Mathf.Ceil(promptText.GetPreferredValues(label).x);
            var textRect = (RectTransform)promptText.transform;

            bool hasCap = !string.IsNullOrEmpty(cap);
            if (keyCap != null) keyCap.gameObject.SetActive(hasCap);

            if (hasCap)
            {
                keyText.text = cap;
                // "LMB" will not fit a square cap; widen it rather than clipping the letters.
                float capW = cap.Length > 1 ? CapSize + (cap.Length - 1) * 11f : CapSize;
                keyCap.sizeDelta = new Vector2(capW, CapSize);
                if (keyCapImage != null)
                    keyCapImage.color = available
                        ? new Color(0.10f, 0.10f, 0.11f, 0.95f)
                        : new Color(0.08f, 0.08f, 0.09f, 0.70f);
                keyText.color = available ? CapInk : RefuseInk;

                float inner = capW + Gap + textW;
                plate.sizeDelta = new Vector2(inner + PadX * 2f, PlateH);
                keyCap.anchoredPosition = new Vector2(-inner / 2f + capW / 2f, 0f);
                textRect.sizeDelta = new Vector2(textW + 4f, 30f);
                textRect.anchoredPosition = new Vector2(inner / 2f - textW / 2f, 0f);
            }
            else
            {
                plate.sizeDelta = new Vector2(textW + PadX * 2f, PlateH);
                textRect.sizeDelta = new Vector2(textW + 4f, 30f);
                textRect.anchoredPosition = Vector2.zero;
            }

            if (plateImage != null)
                plateImage.color = available
                    ? new Color(0.02f, 0.02f, 0.025f, 0.72f)
                    : new Color(0.02f, 0.02f, 0.025f, 0.55f);
        }
    }
}
