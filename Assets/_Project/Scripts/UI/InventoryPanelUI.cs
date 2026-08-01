using LastWard.Player;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// The Inventory tab of the F3 panel: what you are carrying, how full the bag is, and a Drop
    /// button against every single item.
    ///
    /// This exists because carry limits without visibility are just punishment. With only slots 1 and
    /// 2 selectable, anything that landed in a later slot could never be dropped at all — you could
    /// reach the corridor holding a spent key with no way to get rid of it, and no way to even tell
    /// that was what was blocking you. Choosing what to carry is the mechanic; you cannot choose what
    /// you cannot see.
    ///
    /// Builds its own canvas and reads <see cref="PlayerInventory.Local"/> directly, so it needs no
    /// scene wiring and survives the player object being replaced on respawn.
    /// </summary>
    public class InventoryPanelUI : MonoBehaviour
    {
        public static InventoryPanelUI Instance { get; private set; }

        private CanvasGroup group;
        private RectTransform listRoot;
        private Text header;
        private Text capacity;
        private readonly System.Collections.Generic.List<GameObject> rows =
            new System.Collections.Generic.List<GameObject>();

        private bool shown;
        private Font font;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("InventoryCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above GameMenuUI's 150. At 60 it drew behind the menu's own backdrop, so opening
            // the Inventory tab showed a dimmed ghost of the list under a solid panel.
            canvas.sortingOrder = 160;
            MenuTheme.ScaleCanvas(canvasGO);
            canvasGO.AddComponent<GraphicRaycaster>();
            group = canvasGO.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            // Sized and placed to sit in the menu's right-hand content column rather than dead
            // centre, so the two panels read as one screen instead of two stacked windows.
            var panel = MakeRect(canvasGO.transform, new Vector2(660f, 470f), new Color(0f, 0f, 0f, 0f));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(200f, -30f);

            header = MakeLabel(panel, "CARRYING", 20, TextAnchor.UpperLeft,
                new Vector2(500f, 28f), new Vector2(16f, -10f));
            capacity = MakeLabel(panel, "", 16, TextAnchor.UpperLeft,
                new Vector2(500f, 22f), new Vector2(16f, -38f));

            var listGO = new GameObject("List");
            listGO.transform.SetParent(panel, false);
            listRoot = listGO.AddComponent<RectTransform>();
            listRoot.anchorMin = new Vector2(0f, 1f);
            listRoot.anchorMax = new Vector2(0f, 1f);
            listRoot.pivot = new Vector2(0f, 1f);
            listRoot.anchoredPosition = new Vector2(16f, -74f);
            listRoot.sizeDelta = new Vector2(620f, 360f);
        }

        /// <summary>Called by ControlsPanelUI when the Inventory tab is active.</summary>
        public void SetShown(bool value)
        {
            shown = value;
            if (group == null) return;
            group.alpha = value ? 1f : 0f;
            group.blocksRaycasts = value;
            if (!value) return;
            lastSignature = Signature();
            Rebuild();
        }

        // Rebuilt ONLY when something changes. Doing it every frame destroyed and recreated every
        // row continuously, so a Button never survived long enough for press-and-release to land on
        // the same object — which is why DROP could not be clicked at all.
        private int lastSignature = -1;

        private void Update()
        {
            if (!shown) return;
            int sig = Signature();
            if (sig == lastSignature) return;
            lastSignature = sig;
            Rebuild();
        }

        /// <summary>Cheap hash of what is carried and what is selected, to detect real changes.</summary>
        private int Signature()
        {
            var inv = PlayerInventory.Local;
            if (inv == null) return 0;
            int h = 17 + inv.SelectedSlot * 31;
            for (int i = 0; i < inv.Capacity; i++)
            {
                string id = inv.GetSlotAt(i);
                h = h * 31 + (id == null ? 0 : id.GetHashCode());
            }
            return h;
        }

        private void Rebuild()
        {
            var inv = PlayerInventory.Local;
            foreach (var r in rows) if (r != null) Destroy(r);
            rows.Clear();
            if (inv == null || capacity == null) return;

            float pct = inv.BulkFraction * 100f;
            capacity.text = $"Bag {pct:0}% full";
            capacity.color = pct >= 90f ? new Color(0.9f, 0.3f, 0.2f)
                : pct >= 65f ? new Color(0.9f, 0.75f, 0.25f)
                : new Color(0.7f, 0.75f, 0.7f);

            int shownRows = 0;
            for (int i = 0; i < inv.Capacity; i++)
            {
                string id = inv.GetSlotAt(i);
                if (string.IsNullOrEmpty(id)) continue;
                MakeRow(id, i, shownRows++);
            }

            if (shownRows == 0)
                rows.Add(MakeLabel(listRoot, "carrying nothing", 12, TextAnchor.UpperLeft,
                    new Vector2(380f, 20f), new Vector2(0f, 0f)).gameObject);
        }

        private void MakeRow(string itemId, int slotIndex, int row)
        {
            var def = ItemCatalog.Get(itemId);
            float y = -row * 26f;

            bool selected = PlayerInventory.Local != null && PlayerInventory.Local.SelectedSlot == slotIndex;

            var rowGO = new GameObject($"Row_{itemId}");
            rowGO.transform.SetParent(listRoot, false);
            // The row itself is a button: clicking it selects that item, so G drops what you chose
            // rather than whatever happened to be first. The highlight is the only feedback there is
            // for which slot G is pointed at.
            var rowImg = rowGO.AddComponent<Image>();
            rowImg.color = selected ? new Color(0.22f, 0.24f, 0.20f, 0.9f) : new Color(1f, 1f, 1f, 0.03f);
            int selIndex = slotIndex;
            rowGO.AddComponent<Button>().onClick.AddListener(() =>
                PlayerInventory.Local?.SelectSlotIndex(selIndex));

            var rt = rowGO.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(620f, 30f);
            rows.Add(rowGO);

            int left = PlayerInventory.Local != null ? PlayerInventory.Local.UsesLeft(itemId) : -1;
            string wear = left >= 0 ? $"   <color=#C08040>{left} use(s) left</color>" : string.Empty;
            string mark = selected ? "<color=#B8C8A0>> </color>" : "  ";
            MakeLabel(rt, $"{mark}{def.Display}   <color=#7A8A7A>({def.Bulk * 100f:0}% of bag)</color>{wear}",
                12, TextAnchor.MiddleLeft, new Vector2(320f, 24f), new Vector2(0f, 0f));

            // One button per item, so the choice is explicit rather than "whatever is selected".
            var btnGO = new GameObject("Drop");
            btnGO.transform.SetParent(rt, false);
            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.35f, 0.12f, 0.10f, 0.9f);
            var brt = btnGO.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.sizeDelta = new Vector2(78f, 26f);
            brt.anchoredPosition = new Vector2(0f, 0f);

            MakeLabel(brt, "DROP", 11, TextAnchor.MiddleCenter, new Vector2(64f, 20f), Vector2.zero);

            int captured = slotIndex;
            btnGO.AddComponent<Button>().onClick.AddListener(() =>
            {
                PlayerInventory.Local?.DropSlot(captured);
                Rebuild();
            });
        }

        private RectTransform MakeRect(Transform parent, Vector2 size, Color colour)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = colour;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            return rt;
        }

        private Text MakeLabel(Transform parent, string text, int size, TextAnchor anchor,
            Vector2 dims, Vector2 pos)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.text = text;
            t.alignment = anchor;
            t.supportRichText = true;
            t.color = new Color(0.88f, 0.88f, 0.85f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = dims;
            rt.anchoredPosition = pos;
            return t;
        }
    }
}
