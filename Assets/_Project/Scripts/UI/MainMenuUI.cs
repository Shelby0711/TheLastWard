using System.Collections.Generic;
using LastWard.Audio;
using LastWard.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The title screen, and everything reachable from it.
    ///
    /// Structured as pages rather than one screen, because the old menu put a join-code field in
    /// front of every player on launch — including the majority who are about to press Start. A field
    /// you must ignore is worse than a field you must go and find: it asks a question the player has
    /// not been given a reason to answer yet.
    ///
    /// So: <b>Start / Co-op / Settings / Credits / Quit</b>, and the code field lives two clicks deep,
    /// behind Co-op → Join, where the player has already declared what they are trying to do.
    ///
    /// Self-building — no prefab, no scene wiring. Consistent with the rest of the project and it
    /// means a rebuild can never leave a half-connected menu behind.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        public static MainMenuUI Instance { get; private set; }

        private enum Page { Root, Coop, Join, Settings, Credits, Quit }

        private CanvasGroup group;
        private readonly Dictionary<Page, RectTransform> pages = new Dictionary<Page, RectTransform>();
        private Page current = Page.Root;
        private TMP_InputField codeField;
        private TextMeshProUGUI statusLine;
        private AudioSource ambience;
        private Image vignette;

        private void Awake()
        {
            Instance = this;
            AudioSettingsService.Load();
            Build();
            Show(Page.Root);
        }

        private void Start()
        {
            if (NetworkSessionManager.Instance == null) return;
            NetworkSessionManager.Instance.StatusChanged += s =>
            {
                if (statusLine != null) statusLine.text = s;
            };
            // A dropped session must land you back at the title, not in an empty world.
            NetworkSessionManager.Instance.Disconnected += () => { SetVisible(true); Show(Page.Root); };
        }

        // ------------------------------------------------------------------ construction

        private void Build()
        {
            var canvasGO = new GameObject("MainMenuCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;                      // above every HUD layer
            MenuTheme.ScaleCanvas(canvasGO);
            canvasGO.AddComponent<GraphicRaycaster>();
            group = canvasGO.AddComponent<CanvasGroup>();

            BuildBackground(canvasGO.transform);

            var title = MenuTheme.Text(canvasGO.transform, "Title", "THE LAST WARD",
                MenuTheme.TitleSize, MenuTheme.Ink, TextAlignmentOptions.Left);
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(1100f, 110f);
            trt.anchoredPosition = new Vector2(120f, -110f);
            title.characterSpacing = 22f;
            canvasGO.AddComponent<TitleFlicker>().Bind(title);

            var sub = MenuTheme.Text(canvasGO.transform, "Sub", "nobody leaves the building unsigned",
                MenuTheme.BodySize, MenuTheme.Dim, TextAlignmentOptions.Left);
            var srt = sub.rectTransform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(900f, 30f);
            srt.anchoredPosition = new Vector2(126f, -196f);

            BuildRoot(canvasGO.transform);
            BuildCoop(canvasGO.transform);
            BuildJoin(canvasGO.transform);
            BuildSettings(canvasGO.transform);
            BuildCredits(canvasGO.transform);
            BuildQuitConfirm(canvasGO.transform);

            statusLine = MenuTheme.Text(canvasGO.transform, "Status", "", MenuTheme.BodySize, MenuTheme.Dim,
                TextAlignmentOptions.Left);
            var strt = statusLine.rectTransform;
            strt.anchorMin = strt.anchorMax = new Vector2(0f, 0f);
            strt.pivot = new Vector2(0f, 0f);
            strt.sizeDelta = new Vector2(900f, 28f);
            strt.anchoredPosition = new Vector2(126f, 48f);

            BuildAmbience();
        }

        private void BuildBackground(Transform parent)
        {
            var bgRt = MenuTheme.Rect("Background", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bg = bgRt.gameObject.AddComponent<RawImage>();
            bg.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("UI/MainMenuBackground");
            if (tex != null) bg.texture = tex;
            bg.color = new Color(0.55f, 0.55f, 0.58f);      // knocked back so text stays readable

            // Two overlays, not one: a flat scrim for legibility and a heavy vignette for mood.
            // A single dark layer strong enough to do both would grey the photograph out entirely.
            MenuTheme.Solid(parent, "Scrim", new Color(0f, 0f, 0f, 0.45f), stretch: true);
            var vg = MenuTheme.Solid(parent, "Vignette", new Color(0f, 0f, 0f, 0f), stretch: true);
            vignette = vg;
            vg.sprite = VignetteSprite();
            vg.type = Image.Type.Simple;
            vg.color = new Color(0f, 0f, 0f, 0.85f);
        }

        private static Sprite vignetteCache;
        private static Sprite VignetteSprite()
        {
            if (vignetteCache != null) return vignetteCache;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x / (float)(S - 1)) * 2f - 1f;
                    float dy = (y / (float)(S - 1)) * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    px[y * S + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d * 1.35f - 0.28f)));
                }
            tex.SetPixels(px);
            tex.Apply();
            vignetteCache = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            return vignetteCache;
        }

        // Every page hangs from the TOP-LEFT, below the title, and everything inside grows DOWNWARD.
        // The first version pivoted pages from the bottom while positioning their contents from the
        // top, so every button sat several hundred pixels above its own page and off the screen. The
        // menu rendered perfectly; it just was not anywhere anyone could see it.
        private RectTransform NewPage(Transform parent, Page page)
        {
            var rt = MenuTheme.Rect("Page_" + page, parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(1240f, 640f), new Vector2(126f, -250f));
            rt.pivot = new Vector2(0f, 1f);
            pages[page] = rt;
            return rt;
        }

        /// <summary>Pins a child to its page's top-left. y is measured DOWNWARD, like reading.</summary>
        private static RectTransform Place(Component c, float y, float x = 0f)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        private static void Stack(IList<Button> items, float top = 0f)
        {
            for (int i = 0; i < items.Count; i++) Place(items[i], top + i * 54f);
        }

        private void BuildRoot(Transform parent)
        {
            var p = NewPage(parent, Page.Root);
            var start = MenuTheme.Item(p, "Start New Game");
            var coop = MenuTheme.Item(p, "Co-op");
            var settings = MenuTheme.Item(p, "Settings");
            var credits = MenuTheme.Item(p, "Credits");
            var quit = MenuTheme.Item(p, "Quit");
            Stack(new[] { start, coop, settings, credits, quit });

            // Solo is still a hosted session — the entities are server-authoritative, so there is no
            // "offline" path that would not mean a second implementation of everything.
            start.onClick.AddListener(() => BeginSession("HOSTING", () => NetworkSessionManager.Instance?.Host()));
            coop.onClick.AddListener(() => Show(Page.Coop));
            settings.onClick.AddListener(() => Show(Page.Settings));
            credits.onClick.AddListener(() => Show(Page.Credits));
            quit.onClick.AddListener(() => Show(Page.Quit));
        }

        private void BuildCoop(Transform parent)
        {
            var p = NewPage(parent, Page.Coop);
            Place(MenuTheme.Text(p, "Head", "CO-OP", 34f, MenuTheme.Ink), 0f);
            var host = MenuTheme.Item(p, "Host a Session");
            var join = MenuTheme.Item(p, "Join with Code");
            var back = MenuTheme.Item(p, "Back");
            Stack(new[] { host, join, back }, 60f);

            host.onClick.AddListener(() => BeginSession("HOSTING", () => NetworkSessionManager.Instance?.Host()));
            join.onClick.AddListener(() => Show(Page.Join));
            back.onClick.AddListener(() => Show(Page.Root));
        }

        private void BuildJoin(Transform parent)
        {
            var p = NewPage(parent, Page.Join);
            Place(MenuTheme.Text(p, "Head", "JOIN", 34f, MenuTheme.Ink), 0f);
            Place(MenuTheme.Text(p, "Hint", "enter the code the host gave you",
                MenuTheme.BodySize, MenuTheme.Dim), 42f);

            // The field exists only on this page. Two clicks deep, after the player has said what
            // they are trying to do.
            var fieldRt = MenuTheme.Rect("CodeField", p, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(420f, 52f), new Vector2(0f, -84f));
            fieldRt.pivot = new Vector2(0f, 1f);
            var fieldBg = fieldRt.gameObject.AddComponent<Image>();
            fieldBg.color = new Color(0.06f, 0.06f, 0.07f, 0.9f);

            var textRt = MenuTheme.Rect("Text", fieldRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            textRt.offsetMin = new Vector2(14f, 6f);
            textRt.offsetMax = new Vector2(-14f, -6f);
            var text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = 26f;
            text.color = MenuTheme.Ink;
            text.alignment = TextAlignmentOptions.Left;

            codeField = fieldRt.gameObject.AddComponent<TMP_InputField>();
            codeField.textComponent = text;
            codeField.textViewport = textRt;
            codeField.characterLimit = 12;

            var connect = MenuTheme.Item(p, "Connect");
            var back = MenuTheme.Item(p, "Back");
            Place(connect, 156f);
            Place(back, 210f);

            connect.onClick.AddListener(() =>
            {
                string code = codeField != null ? codeField.text.Trim() : "";
                if (string.IsNullOrEmpty(code))
                {
                    if (statusLine != null) statusLine.text = "Enter a code first.";
                    return;
                }
                BeginSession("JOINING", () => NetworkSessionManager.Instance?.Join(code));
            });
            back.onClick.AddListener(() => Show(Page.Coop));
        }

        private void BuildSettings(Transform parent)
        {
            var p = NewPage(parent, Page.Settings);
            Place(MenuTheme.Text(p, "Head", "SETTINGS", 34f, MenuTheme.Ink), 0f);
            SettingsPanel.Build(p, new Vector2(0f, -60f));

            var back = MenuTheme.Item(p, "Back");
            Place(back, 250f);
            back.onClick.AddListener(() => Show(Page.Root));
        }

        private void BuildCredits(Transform parent)
        {
            var p = NewPage(parent, Page.Credits);
            // Full-height and clear of the title: this is the one page that is a document rather
            // than a short list of choices.
            p.sizeDelta = new Vector2(1500f, 900f);
            p.anchoredPosition = new Vector2(126f, -230f);
            Place(MenuTheme.Text(p, "Head", "CREDITS", 34f, MenuTheme.Ink), 0f);

            var body = MenuTheme.Text(p, "Body", CreditsText.Body, 17f, MenuTheme.Dim);
            body.characterSpacing = 0f;
            body.lineSpacing = 10f;
            // TopLeft, not Left. TMP treats "Left" as left-MIDDLE, so a long block centres itself in
            // its box and overflows upward — the credits were growing back over the title.
            body.alignment = TMPro.TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = false;
            var brt = Place(body, 52f);
            brt.sizeDelta = new Vector2(1200f, 520f);

            var back = MenuTheme.Item(p, "Back");
            Place(back, 540f);
            back.onClick.AddListener(() => Show(Page.Root));
        }

        private void BuildQuitConfirm(Transform parent)
        {
            var p = NewPage(parent, Page.Quit);
            Place(MenuTheme.Text(p, "Head", "QUIT TO DESKTOP?", 34f, MenuTheme.Ink), 0f);
            var yes = MenuTheme.Item(p, "Yes, quit");
            var no = MenuTheme.Item(p, "Stay");
            Stack(new[] { yes, no }, 60f);
            yes.onClick.AddListener(() =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            no.onClick.AddListener(() => Show(Page.Root));
        }

        private void BuildAmbience()
        {
            var go = new GameObject("MenuAmbience");
            go.transform.SetParent(transform, false);
            ambience = go.AddComponent<AudioSource>();
            ambience.clip = Resources.Load<AudioClip>("SFX/Menu_Background_Music");
            ambience.loop = true;
            ambience.spatialBlend = 0f;
            ambience.playOnAwake = false;
            // Ignores the pause that the in-game menu applies, so returning to the title from a
            // paused run does not leave the music frozen.
            ambience.ignoreListenerPause = true;
            AudioSettingsService.RegisterMusic(ambience);
            if (ambience.clip != null) ambience.Play();
            else Debug.LogWarning("[Menu] Resources/SFX/Menu_Background_Music not found - the title " +
                                  "screen will be silent. Check the clip imported.");
        }

        // ------------------------------------------------------------------ state

        private void Show(Page page)
        {
            current = page;
            foreach (var kv in pages) kv.Value.gameObject.SetActive(kv.Key == page);
        }

        /// <summary>
        /// Hands off to the loading screen and then starts the session.
        ///
        /// The order matters: the loading screen has to be up and drawing BEFORE the first await in
        /// Host/Join, or the very first phase change lands on a screen nobody can see yet. If the
        /// loading screen is somehow absent the session still starts — it degrades to the old
        /// behaviour rather than becoming an unclickable menu.
        /// </summary>
        private void BeginSession(string title, System.Action start)
        {
            LoadingScreen.Instance?.Begin(title);
            SetVisible(false);
            start();
        }

        public void SetVisible(bool on)
        {
            if (group == null) return;
            group.alpha = on ? 1f : 0f;
            group.blocksRaycasts = on;
            group.interactable = on;
            if (ambience != null)
            {
                if (on && !ambience.isPlaying && ambience.clip != null) ambience.Play();
                else if (!on && ambience.isPlaying) ambience.Stop();
            }
            if (on)
            {
                // The title screen and the loading screen are mutually exclusive by definition, and
                // the loading canvas sorts above this one — so a session that drops mid-connect
                // would otherwise put the menu behind a loading screen that never finishes.
                LoadingScreen.Instance?.Cancel();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Show(Page.Root);
            }
        }

        private void Update()
        {
            // Escape backs out one level rather than doing nothing, so the code field is never a
            // dead end for a player who opened it by mistake.
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (group == null || group.alpha < 0.5f || kb == null) return;
            if (!kb.escapeKey.wasPressedThisFrame) return;
            if (current == Page.Join) Show(Page.Coop);
            else if (current != Page.Root) Show(Page.Root);
        }
    }

    /// <summary>A slow, irregular dim on the title. Steady text reads as a placeholder.</summary>
    public class TitleFlicker : MonoBehaviour
    {
        private TextMeshProUGUI target;
        private float next;
        public void Bind(TextMeshProUGUI t) => target = t;

        private void Update()
        {
            if (target == null) return;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Random.Range(0.9f, 4.5f);
            StartCoroutine(Dip());
        }

        private System.Collections.IEnumerator Dip()
        {
            var c = target.color;
            for (int i = 0; i < Random.Range(1, 4); i++)
            {
                target.color = new Color(c.r, c.g, c.b, Random.Range(0.35f, 0.6f));
                yield return new WaitForSecondsRealtime(Random.Range(0.03f, 0.09f));
                target.color = c;
                yield return new WaitForSecondsRealtime(Random.Range(0.04f, 0.12f));
            }
        }
    }
}
