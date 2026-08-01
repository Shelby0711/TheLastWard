using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// One place that decides what every menu looks like.
    ///
    /// The menus are built in code rather than as prefabs — the whole project is, so that a rebuild
    /// is reproducible and nothing depends on a scene asset somebody edited by hand. That only works
    /// if the styling lives somewhere shared: five screens each inventing their own font size and
    /// grey is how a UI ends up looking assembled rather than designed.
    ///
    /// The look: near-black, desaturated, wide-tracked capitals, and exactly one accent colour. Horror
    /// menus fail by being busy — the restraint is the style, and the single red is worth more when
    /// nothing else competes with it.
    /// </summary>
    public static class MenuTheme
    {
        public static readonly Color Ink = new Color(0.82f, 0.80f, 0.76f);       // body text
        public static readonly Color Dim = new Color(0.42f, 0.41f, 0.39f);       // secondary
        public static readonly Color Accent = new Color(0.72f, 0.13f, 0.10f);    // the one red
        public static readonly Color Panel = new Color(0.02f, 0.02f, 0.025f, 0.86f);
        public static readonly Color Hairline = new Color(0.30f, 0.29f, 0.27f, 0.55f);

        public const float TitleSize = 74f;
        // Bumped from 26/18. At the original sizes the menus read as annotations rather than as
        // the screen itself, and the wide tracking made small type harder still.
        public const float ItemSize = 30f;
        public const float BodySize = 21f;

        public static RectTransform Rect(string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        public static Image Solid(Transform parent, string name, Color c, bool stretch = false)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string content,
            float size, Color colour, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(560f, size * 1.6f), Vector2.zero);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.raycastTarget = false;
            // Tracking, not bold. Wide capitals read as institutional signage, which is the register
            // this building's paperwork is written in.
            t.characterSpacing = 8f;
            return t;
        }

        /// <summary>The resolution every screen in the game is laid out against.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

        /// <summary>
        /// Attaches a correctly configured CanvasScaler.
        ///
        /// Five of the self-building HUD panels set ScaleWithScreenSize and then never set a
        /// reference resolution, so they all inherited Unity's 800x600 default and rendered 2.4x
        /// oversized on a 1080p screen — which is why the inventory list ran off the right edge of
        /// the display and buried its own Drop control, while the menu beside it (built through
        /// EditorBuildKit, which does set it) was the right size. The two panels were never
        /// duplicates; one of them was simply enormous.
        ///
        /// Anything that builds its own canvas must come through here, so there is one place this
        /// can be wrong rather than six.
        /// </summary>
        public static CanvasScaler ScaleCanvas(GameObject canvasGO)
        {
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            // Match height. These are horror HUDs on ultrawide monitors as often as not, and
            // matching width would blow the type up to fill the extra pixels.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            return scaler;
        }

        /// <summary>
        /// A menu row: a label that brightens and grows a red bar when pointed at.
        ///
        /// No button background and no border. A filled rectangle would sit on top of the photograph
        /// and flatten it; the hairline and the shift in colour are enough to read as interactive,
        /// and they leave the image doing the work.
        /// </summary>
        public static Button Item(Transform parent, string label, float width = 420f)
        {
            var rt = Rect("Item_" + label, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(width, 46f), Vector2.zero);

            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);          // invisible, but clickable
            var btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var bar = Solid(rt, "Bar", Accent);
            var barRt = bar.rectTransform;
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(0f, 0.5f);
            barRt.sizeDelta = new Vector2(3f, 26f);
            barRt.anchoredPosition = new Vector2(2f, 0f);
            bar.color = new Color(Accent.r, Accent.g, Accent.b, 0f);

            var text = Text(rt, "Label", label.ToUpperInvariant(), ItemSize, Dim);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(22f, 0f);
            trt.offsetMax = new Vector2(0f, 0f);
            text.alignment = TextAlignmentOptions.Left;

            var hover = rt.gameObject.AddComponent<MenuItemHover>();
            hover.Bind(text, bar);
            return btn;
        }

        /// <summary>A thin horizontal rule. Named Rule, not Hairline — that is the colour.</summary>
        public static void RuleLine(Transform parent, float width, float y)
        {
            var line = Solid(parent, "Rule", Hairline);
            var rt = line.rectTransform;
            rt.sizeDelta = new Vector2(width, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
        }
    }

    /// <summary>Brightens a menu row and slides its accent bar in on hover.</summary>
    public class MenuItemHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private TextMeshProUGUI label;
        private Image bar;
        private bool over;

        public void Bind(TextMeshProUGUI t, Image b) { label = t; bar = b; }

        public void OnPointerEnter(PointerEventData e) => over = true;
        public void OnPointerExit(PointerEventData e) => over = false;

        private void Update()
        {
            if (label == null) return;
            // Unscaled: menus run while the game is paused at timeScale 0.
            float k = Time.unscaledDeltaTime * 12f;
            label.color = Color.Lerp(label.color, over ? MenuTheme.Ink : MenuTheme.Dim, k);
            if (bar != null)
            {
                var c = bar.color;
                c.a = Mathf.Lerp(c.a, over ? 1f : 0f, k);
                bar.color = c;
                var rt = bar.rectTransform;
                rt.sizeDelta = new Vector2(3f, Mathf.Lerp(rt.sizeDelta.y, over ? 30f : 20f, k));
            }
            var lrt = label.rectTransform;
            lrt.offsetMin = new Vector2(Mathf.Lerp(lrt.offsetMin.x, over ? 34f : 22f, k), 0f);
        }
    }
}
