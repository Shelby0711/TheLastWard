using System.Collections.Generic;
using LastWard.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The in-game menu, on F3. Inventory, Controls, Audio, and the way out.
    ///
    /// Replaces the old panel, which was a wall of text with a hidden Tab to flip to a second wall of
    /// text. Tabs down the left, content on the right — the shape a player already knows, so nothing
    /// about the menu itself has to be learned.
    ///
    /// It does NOT pause. This is a co-op game: three other people are still being hunted while you
    /// read your controls, and a menu that stops the world for you alone would either desync or lie.
    /// The cursor is released and the world keeps running, which is also why Exit needs a confirm —
    /// leaving is not a decision you should be able to make with one stray click.
    /// </summary>
    public class GameMenuUI : MonoBehaviour
    {
        public static GameMenuUI Instance { get; private set; }

        private enum Tab { Inventory, Controls, Audio, Exit }

        private CanvasGroup group;
        private readonly Dictionary<Tab, RectTransform> panels = new Dictionary<Tab, RectTransform>();
        private readonly Dictionary<Tab, TextMeshProUGUI> tabLabels = new Dictionary<Tab, TextMeshProUGUI>();
        private Tab current = Tab.Inventory;
        private bool visible;
        private PlayerInputReader subscribedTo;

        private const string Controls =
            "<b>W A S D</b>      Move\n" +
            "<b>Mouse</b>        Look\n" +
            "<b>Shift</b>        Sprint  <i>— louder; it can hear you</i>\n" +
            "<b>C / Ctrl</b>     Crouch\n\n" +
            "<b>E</b>            Interact — pick up, read, open, push\n" +
            "<b>Q</b>            Hide / come out\n" +
            "<b>Left Mouse</b>   Swing carried weapon  <i>— one hit, then it's gone</i>\n" +
            "<b>F</b>            Torch  <i>— 5 bars; leaving it on burns them faster</i>\n" +
            "<b>V</b>            Hold breath  <i>— silent, while the air lasts</i>\n" +
            "<b>1 / 2</b>        Inventory slots\n" +
            "<b>G</b>            Drop selected item\n" +
            "<b>F1 / F2 / F4</b> Emote  <i>— quiet, but not silent</i>\n\n" +
            "<b>Q / E</b>        While dead: switch who you're watching\n" +
            "<b>Left Mouse</b>   While dead: ping for the living\n" +
            "<b>R</b>            Skip the wait after a run ends\n" +
            "<b>F3</b>           Open or close this menu";

        private void Awake()
        {
            Instance = this;
            Build();
            Apply(false);
        }

        private void Build()
        {
            var canvasGO = new GameObject("GameMenuCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            MenuTheme.ScaleCanvas(canvasGO);
            canvasGO.AddComponent<GraphicRaycaster>();
            group = canvasGO.AddComponent<CanvasGroup>();

            MenuTheme.Solid(canvasGO.transform, "Dim", new Color(0f, 0f, 0f, 0.72f), stretch: true);

            var frame = MenuTheme.Rect("Frame", canvasGO.transform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(1180f, 700f), Vector2.zero);
            var frameBg = frame.gameObject.AddComponent<Image>();
            frameBg.color = MenuTheme.Panel;

            MenuTheme.Text(frame, "Head", "PAUSED", 30f, MenuTheme.Ink).rectTransform
                .anchoredPosition = new Vector2(-190f, 300f);
            MenuTheme.RuleLine(frame, 1080f, 268f);

            // Tabs down the left.
            var tabs = new[] { Tab.Inventory, Tab.Controls, Tab.Audio, Tab.Exit };
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                var btn = MenuTheme.Item(frame, tab.ToString(), 280f);
                var rt = (RectTransform)btn.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(44f, -110f - i * 54f);
                tabLabels[tab] = btn.GetComponentInChildren<TextMeshProUGUI>();
                var captured = tab;
                btn.onClick.AddListener(() => Select(captured));
            }

            // A vertical rule between the tabs and the content, so the two halves read as one panel
            // rather than two things that happen to be near each other.
            var rule = MenuTheme.Solid(frame, "Divider", MenuTheme.Hairline);
            rule.rectTransform.sizeDelta = new Vector2(1f, 470f);
            rule.rectTransform.anchoredPosition = new Vector2(-232f, -50f);

            BuildInventory(frame);
            BuildControls(frame);
            BuildAudio(frame);
            BuildExit(frame);
            Select(Tab.Inventory);
        }

        private RectTransform NewPanel(Transform frame, Tab tab)
        {
            var rt = MenuTheme.Rect("Panel_" + tab, frame, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(820f, 500f), new Vector2(380f, -110f));
            rt.pivot = new Vector2(0f, 1f);
            panels[tab] = rt;
            return rt;
        }

        private void BuildInventory(Transform frame)
        {
            var p = NewPanel(frame, Tab.Inventory);
            MenuTheme.Text(p, "Hint",
                "Everything you are carrying. Drop what you no longer need — the bag is small, and " +
                "choosing what to carry is most of the co-op.", 17f, MenuTheme.Dim)
                .rectTransform.anchoredPosition = new Vector2(300f, -430f);
        }

        private void BuildControls(Transform frame)
        {
            var p = NewPanel(frame, Tab.Controls);
            var t = MenuTheme.Text(p, "Body", Controls, 19f, MenuTheme.Dim);
            t.characterSpacing = 0f;
            t.lineSpacing = 6f;
            t.alignment = TMPro.TextAlignmentOptions.TopLeft;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(800f, 470f);
            rt.anchoredPosition = Vector2.zero;
        }

        private void BuildAudio(Transform frame)
        {
            var p = NewPanel(frame, Tab.Audio);
            SettingsPanel.Build(p, new Vector2(0f, -10f));
        }

        private void BuildExit(Transform frame)
        {
            var p = NewPanel(frame, Tab.Exit);
            MenuTheme.Text(p, "Warn",
                "Leave this run and return to the main menu?\n\n" +
                "<color=#B82118>The run ends here.</color> Nothing is saved, and anyone still alive " +
                "in your session will be left without you.", 19f, MenuTheme.Dim)
                .rectTransform.anchoredPosition = new Vector2(300f, -40f);

            var yes = MenuTheme.Item(p, "Yes, leave the building");
            var no = MenuTheme.Item(p, "Stay");
            var yrt = (RectTransform)yes.transform;
            yrt.anchorMin = yrt.anchorMax = new Vector2(0f, 1f);
            yrt.pivot = new Vector2(0f, 1f);
            yrt.anchoredPosition = new Vector2(0f, -140f);
            var nrt = (RectTransform)no.transform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 1f);
            nrt.pivot = new Vector2(0f, 1f);
            nrt.anchoredPosition = new Vector2(0f, -194f);

            yes.onClick.AddListener(LeaveToMenu);
            no.onClick.AddListener(() => Select(Tab.Inventory));
        }

        private void LeaveToMenu()
        {
            Apply(false);
            // Leave(), not NetworkManager.Shutdown(): the session manager also has to release the
            // Relay allocation and the lobby, and a raw shutdown leaves both dangling.
            var session = LastWard.Net.NetworkSessionManager.Instance;
            if (session != null) session.Leave();
            MainMenuUI.Instance?.SetVisible(true);
        }

        private void Select(Tab tab)
        {
            current = tab;
            foreach (var kv in panels) kv.Value.gameObject.SetActive(kv.Key == tab);
            foreach (var kv in tabLabels)
                if (kv.Value != null)
                    kv.Value.color = kv.Key == tab ? MenuTheme.Accent : MenuTheme.Dim;
            // The inventory list is its own component; this menu only decides when it is on screen.
            InventoryPanelUI.Instance?.SetShown(visible && tab == Tab.Inventory);
        }

        private void Update()
        {
            // The reader is replaced on respawn, so the subscription follows whichever is local now.
            var reader = PlayerInputReader.Local;
            if (reader != subscribedTo)
            {
                if (subscribedTo != null) subscribedTo.JournalPressed -= Toggle;
                subscribedTo = reader;
                if (subscribedTo != null) subscribedTo.JournalPressed += Toggle;
            }

            if (!visible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (current != Tab.Inventory) Select(Tab.Inventory);
                else Apply(false);
            }
        }

        private void OnDisable()
        {
            if (subscribedTo != null) subscribedTo.JournalPressed -= Toggle;
            subscribedTo = null;
        }

        private void Toggle() => Apply(!visible);

        private void Apply(bool show)
        {
            bool changed = visible != show;
            visible = show;
            if (group != null)
            {
                group.alpha = show ? 1f : 0f;
                group.blocksRaycasts = show;
                group.interactable = show;
            }
            if (show) Select(Tab.Inventory);
            else InventoryPanelUI.Instance?.SetShown(false);

            if (!changed) return;
            // The cursor has to be released — the tabs and the Drop buttons are clicked, not driven
            // by the movement keys. CursorLockGate keeps this honest against the other panels.
            if (show)
            {
                CursorLockGate.PanelOpened();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                CursorLockGate.PanelClosed();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
