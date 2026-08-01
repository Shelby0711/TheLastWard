using System.Collections.Generic;
using LastWard.Net;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// What the player looks at between pressing Start and standing in the woods.
    ///
    /// Creating a relay session is several seconds of network round trips — sign in, allocate,
    /// create, connect, spawn — and until now the menu simply switched itself off and left a black
    /// screen with nothing moving on it. That reads as a crash, and players close crashed games.
    ///
    /// It is honest about what it is: every phase string here comes from
    /// <see cref="NetworkSessionManager.StatusChanged"/>, so the line under the bar is the real
    /// state of the connection rather than a fake percentage counting up to a number nobody chose.
    /// The bar itself is deliberately indeterminate — the session gives no progress figure, and
    /// inventing one is how you end up sitting at 99% for six seconds.
    ///
    /// <b>It cannot cover a real stall.</b> This animates through the awaits, which is where nearly
    /// all of the wait actually is. The single-frame hitches while NGO spawns the scene's network
    /// objects will still hitch, because nothing driven by the main thread can animate through the
    /// main thread being busy. If those become noticeable they are a separate problem from this one.
    ///
    /// Dismissal is tied to <see cref="PlayerNetworkState.LocalInstance"/> rather than to a timer:
    /// the screen goes away when there is genuinely a player in the world to control, which is the
    /// only definition of "loaded" that cannot be wrong.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Instance { get; private set; }

        private const float CardSeconds = 7.5f;      // long enough to read a long card unhurried
        private const float FadeSeconds = 1.1f;
        private const float MinimumOnScreen = 2.2f;  // so a fast connect is a beat, not a flicker
        private const float StuckAfter = 60f;        // watchdog: never trap the player in here
        private const string ResourceFolder = "Loading";

        private CanvasGroup group;
        private Image backA, backB;
        private RectTransform kenA, kenB;
        private TMP_Text categoryText, bodyText, statusText, titleText, hintText;
        private RectTransform barFill;
        private Button backButton;

        private Sprite[] shots;
        private readonly List<int> shotOrder = new List<int>();
        private readonly List<int> cardOrder = new List<int>();
        private int shotAt, cardAt;

        private bool active, failed;
        private bool showingA = true;
        private float cardTimer, fadeTimer, shownAt, kenPhase;

        private void Awake()
        {
            Instance = this;
            Build();
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (NetworkSessionManager.Instance != null)
                NetworkSessionManager.Instance.StatusChanged -= OnStatus;
        }

        // ------------------------------------------------------------------ lifecycle

        /// <param name="title">"HOSTING" or "JOINING" — what the player asked for.</param>
        public void Begin(string title)
        {
            active = true;
            failed = false;
            shownAt = Time.unscaledTime;
            titleText.text = title;
            statusText.text = "Connecting to services...";
            hintText.text = string.Empty;
            backButton.gameObject.SetActive(false);

            group.alpha = 1f;
            group.blocksRaycasts = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Reshuffle();
            NextCard(immediate: true);
            NextShot(immediate: true);

            if (NetworkSessionManager.Instance != null)
            {
                NetworkSessionManager.Instance.StatusChanged -= OnStatus;
                NetworkSessionManager.Instance.StatusChanged += OnStatus;
            }
        }

        private void OnStatus(string s)
        {
            if (!active) return;
            statusText.text = s;

            // The session manager reports failures through the same channel it reports progress on,
            // so this is where a dead connection surfaces. Stop pretending to load and offer a way
            // back — the alternative is the player staring at a bar that will never finish.
            if (s != null && s.Contains("failed"))
            {
                failed = true;
                hintText.text = "The building did not answer. Check your connection and try again.";
                backButton.gameObject.SetActive(true);
            }
        }

        /// <summary>Abandon the wait — the session dropped, or the player backed out.</summary>
        public void Cancel() => Dismiss();

        private void Dismiss()
        {
            active = false;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            if (NetworkSessionManager.Instance != null)
                NetworkSessionManager.Instance.StatusChanged -= OnStatus;
        }

        private void Update()
        {
            if (!active) return;

            if (!failed)
            {
                // Loaded means there is a player object to control. Not a timer, not a status
                // string — those can both be true while the world is still empty.
                bool ready = PlayerNetworkState.LocalInstance != null &&
                             NetworkManager.Singleton != null &&
                             NetworkManager.Singleton.IsListening;
                if (ready && Time.unscaledTime - shownAt >= MinimumOnScreen)
                {
                    Dismiss();
                    return;
                }

                // A connection that neither succeeds nor reports a failure would otherwise leave
                // the player watching an indeterminate bar forever, with the menu sorted underneath
                // and unreachable — strictly worse than the black screen this replaced. Relay is a
                // few seconds when it works, so a minute is far past any legitimate wait.
                if (Time.unscaledTime - shownAt >= StuckAfter)
                {
                    failed = true;
                    statusText.text = "No response from the session.";
                    hintText.text = "Something went wrong reaching the servers. Try again.";
                    backButton.gameObject.SetActive(true);
                }
            }

            float dt = Time.unscaledDeltaTime;

            // Slow push-in on the visible backdrop. One lerp, no animation system: these are
            // stills, and a still that never moves is indistinguishable from a frozen game, which
            // is the exact impression this screen exists to prevent.
            kenPhase += dt;
            float k = Mathf.Min(1f, kenPhase / (CardSeconds + FadeSeconds));
            var front = showingA ? kenA : kenB;
            float scale = Mathf.Lerp(1.04f, 1.11f, k);
            front.localScale = new Vector3(scale, scale, 1f);
            front.anchoredPosition = new Vector2(Mathf.Lerp(-14f, 14f, k), Mathf.Lerp(6f, -6f, k));

            // Cross-fade between the two stacked images.
            if (fadeTimer > 0f)
            {
                fadeTimer -= dt;
                float t = 1f - Mathf.Clamp01(fadeTimer / FadeSeconds);
                backA.color = new Color(1f, 1f, 1f, showingA ? t : 1f - t);
                backB.color = new Color(1f, 1f, 1f, showingA ? 1f - t : t);
            }

            cardTimer -= dt;
            if (cardTimer <= 0f)
            {
                NextCard(immediate: false);
                NextShot(immediate: false);
            }

            // Indeterminate sweep. It says "still working", which is all it is entitled to say.
            if (!failed)
            {
                float w = 0.22f;
                float p = Mathf.Repeat(Time.unscaledTime * 0.38f, 1f + w) - w;
                barFill.anchorMin = new Vector2(Mathf.Clamp01(p), 0f);
                barFill.anchorMax = new Vector2(Mathf.Clamp01(p + w), 1f);
                barFill.offsetMin = Vector2.zero;
                barFill.offsetMax = Vector2.zero;
            }
        }

        // ------------------------------------------------------------------ content rotation

        // Shuffled bags rather than random picks, so the same card cannot come up twice running and
        // a short wait still shows a different one each time.
        private void Reshuffle()
        {
            shotOrder.Clear();
            cardOrder.Clear();
            if (shots != null) for (int i = 0; i < shots.Length; i++) shotOrder.Add(i);
            for (int i = 0; i < LoadingLore.Cards.Length; i++) cardOrder.Add(i);
            Shuffle(shotOrder);
            Shuffle(cardOrder);
            shotAt = cardAt = 0;
        }

        private static void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void NextCard(bool immediate)
        {
            cardTimer = CardSeconds;
            if (cardOrder.Count == 0) return;
            if (cardAt >= cardOrder.Count) { Shuffle(cardOrder); cardAt = 0; }
            var card = LoadingLore.Cards[cardOrder[cardAt++]];
            categoryText.text = card.Category;
            bodyText.text = card.Body;
        }

        private void NextShot(bool immediate)
        {
            if (shots == null || shots.Length == 0) return;
            if (shotAt >= shotOrder.Count) { Shuffle(shotOrder); shotAt = 0; }
            var sprite = shots[shotOrder[shotAt++]];

            if (immediate)
            {
                showingA = true;
                backA.sprite = sprite;
                backA.color = Color.white;
                backB.color = new Color(1f, 1f, 1f, 0f);
                fadeTimer = 0f;
            }
            else
            {
                // Load into whichever image is currently hidden, then fade to it.
                if (showingA) backB.sprite = sprite; else backA.sprite = sprite;
                showingA = !showingA;
                fadeTimer = FadeSeconds;
            }
            kenPhase = 0f;
        }

        // ------------------------------------------------------------------ construction

        private void Build()
        {
            var canvasGO = new GameObject("LoadingCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;            // above MainMenuUI's 200 and every HUD layer
            MenuTheme.ScaleCanvas(canvasGO);
            canvasGO.AddComponent<GraphicRaycaster>();
            group = canvasGO.AddComponent<CanvasGroup>();

            // Black underneath, so a missing backdrop is a black screen with readable text on it
            // rather than a transparent hole showing the world being built behind.
            MenuTheme.Solid(canvasGO.transform, "Black", Color.black, stretch: true);

            LoadShots();
            backA = MakeBackdrop(canvasGO.transform, "BackA", out kenA);
            backB = MakeBackdrop(canvasGO.transform, "BackB", out kenB);
            backB.color = new Color(1f, 1f, 1f, 0f);

            // Two scrims, and the split matters. The flat overlay is deliberately light: these
            // backdrops are already near-black horror frames, and the 42% it started at buried them
            // to the point that only the blood on the mirror came through. Almost all of the
            // contrast the text needs comes from the heavy band instead, which only covers the
            // bottom of the screen — so the picture stays a picture and the reading stays readable.
            MenuTheme.Solid(canvasGO.transform, "Scrim", new Color(0f, 0f, 0f, 0.16f), stretch: true);
            var band = MenuTheme.Rect("ReadingBand", canvasGO.transform, new Vector2(0f, 0f),
                new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            band.anchorMin = new Vector2(0f, 0f);
            band.anchorMax = new Vector2(1f, 0.42f);
            band.offsetMin = Vector2.zero;
            band.offsetMax = Vector2.zero;
            var bandImg = band.gameObject.AddComponent<Image>();
            bandImg.color = new Color(0f, 0f, 0f, 0.80f);
            bandImg.raycastTarget = false;

            // --- the card ---------------------------------------------------------------
            categoryText = MenuTheme.Text(canvasGO.transform, "Category", "TIP", 17f, MenuTheme.Accent);
            var cat = (RectTransform)categoryText.transform;
            cat.anchorMin = cat.anchorMax = new Vector2(0f, 0f);
            cat.pivot = new Vector2(0f, 0.5f);
            cat.sizeDelta = new Vector2(700f, 24f);
            cat.anchoredPosition = new Vector2(96f, 268f);

            bodyText = MenuTheme.Text(canvasGO.transform, "Body", string.Empty, 24f, MenuTheme.Ink);
            var bod = (RectTransform)bodyText.transform;
            bod.anchorMin = bod.anchorMax = new Vector2(0f, 0f);
            bod.pivot = new Vector2(0f, 1f);
            bod.sizeDelta = new Vector2(1180f, 150f);
            bod.anchoredPosition = new Vector2(96f, 246f);
            bodyText.characterSpacing = 2f;
            bodyText.lineSpacing = 14f;
            bodyText.alignment = TextAlignmentOptions.TopLeft;

            // Built inline rather than via MenuTheme.RuleLine, which centres its rule — this one
            // has to start at the same left margin as the text it sits above.
            var rule = MenuTheme.Rect("Rule", canvasGO.transform, new Vector2(0f, 0f),
                new Vector2(0f, 0f), new Vector2(1180f, 1f), new Vector2(96f, 292f));
            rule.pivot = new Vector2(0f, 0.5f);
            rule.anchoredPosition = new Vector2(96f, 292f);
            var ruleImg = rule.gameObject.AddComponent<Image>();
            ruleImg.color = MenuTheme.Hairline;
            ruleImg.raycastTarget = false;

            // --- the connection state ----------------------------------------------------
            titleText = MenuTheme.Text(canvasGO.transform, "Title", "HOSTING", 30f, MenuTheme.Ink);
            var tit = (RectTransform)titleText.transform;
            tit.anchorMin = tit.anchorMax = new Vector2(0f, 0f);
            tit.pivot = new Vector2(0f, 0.5f);
            tit.sizeDelta = new Vector2(700f, 40f);
            tit.anchoredPosition = new Vector2(96f, 96f);
            titleText.characterSpacing = 12f;

            statusText = MenuTheme.Text(canvasGO.transform, "Status", string.Empty, 18f, MenuTheme.Dim);
            var sta = (RectTransform)statusText.transform;
            sta.anchorMin = sta.anchorMax = new Vector2(0f, 0f);
            sta.pivot = new Vector2(0f, 0.5f);
            sta.sizeDelta = new Vector2(900f, 26f);
            sta.anchoredPosition = new Vector2(96f, 64f);

            hintText = MenuTheme.Text(canvasGO.transform, "Hint", string.Empty, 18f, MenuTheme.Accent);
            var hin = (RectTransform)hintText.transform;
            hin.anchorMin = hin.anchorMax = new Vector2(0f, 0f);
            hin.pivot = new Vector2(0f, 0.5f);
            hin.sizeDelta = new Vector2(1000f, 26f);
            hin.anchoredPosition = new Vector2(96f, 38f);

            // Progress bar, hairline-thin and full width along the very bottom.
            var track = MenuTheme.Rect("BarTrack", canvasGO.transform, new Vector2(0f, 0f),
                new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.offsetMin = Vector2.zero;
            track.offsetMax = new Vector2(0f, 3f);
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.color = new Color(0.16f, 0.15f, 0.14f, 0.9f);
            trackImg.raycastTarget = false;

            barFill = MenuTheme.Rect("BarFill", track, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            var fillImg = barFill.gameObject.AddComponent<Image>();
            fillImg.color = MenuTheme.Accent;
            fillImg.raycastTarget = false;

            // Only ever shown on a failure.
            backButton = MenuTheme.Item(canvasGO.transform, "Back to Title", 320f);
            var bb = (RectTransform)backButton.transform;
            bb.anchorMin = bb.anchorMax = new Vector2(1f, 0f);
            bb.pivot = new Vector2(1f, 0.5f);
            bb.anchoredPosition = new Vector2(-96f, 78f);
            backButton.onClick.AddListener(() =>
            {
                // Tear the half-open session down on the way out. Returning to the title with a
                // dangling relay allocation is how the next Host attempt fails for no visible
                // reason.
                NetworkSessionManager.Instance?.Leave();
                Dismiss();
                MainMenuUI.Instance?.SetVisible(true);
            });
            backButton.gameObject.SetActive(false);
        }

        private Image MakeBackdrop(Transform parent, string name, out RectTransform ken)
        {
            // Anchored slightly outside the screen on all sides so the push-in and drift never
            // expose an edge. The image is set to fill rather than preserve aspect: the sources are
            // all cropped to 16:9 on import, and letterboxing a loading screen looks like a bug.
            ken = MenuTheme.Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ken.anchorMin = Vector2.zero;
            ken.anchorMax = Vector2.one;
            ken.offsetMin = new Vector2(-40f, -40f);
            ken.offsetMax = new Vector2(40f, 40f);
            var img = ken.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = false;
            return img;
        }

        private void LoadShots()
        {
            var textures = Resources.LoadAll<Texture2D>(ResourceFolder);
            if (textures == null || textures.Length == 0)
            {
                Debug.LogWarning($"[Loading] No backdrops under Resources/{ResourceFolder} — " +
                                 "the loading screen will run on black. Text still reads fine.");
                shots = new Sprite[0];
                return;
            }

            shots = new Sprite[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                var t = textures[i];
                shots[i] = Sprite.Create(t, new Rect(0f, 0f, t.width, t.height),
                    new Vector2(0.5f, 0.5f), 100f);
                shots[i].name = t.name;
            }
        }
    }
}
