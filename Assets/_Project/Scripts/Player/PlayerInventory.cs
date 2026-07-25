using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWard.Player
{
    public class PlayerInventory : MonoBehaviour
    {
        public static PlayerInventory Local { get; private set; }

        // Slots are generous; the real limit is BULK. Two separate limits doing two jobs: bulk is a
        // soft budget everything competes for, so taking the crowbar genuinely costs you room, while
        // the per-category caps in ItemCatalog are hard walls that exist to force co-op splits.
        private const int SlotCount = 8;

        /// <summary>How full the bag is, 0..1. Drives the percentage in the inventory panel.</summary>
        public float BulkUsed
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < SlotCount; i++)
                    if (!string.IsNullOrEmpty(slots[i])) t += ItemCatalog.Get(slots[i]).Bulk;
                return t;
            }
        }

        public float BulkFraction => Mathf.Clamp01(BulkUsed / ItemCatalog.BagCapacity);
        public int Capacity => SlotCount;

        /// <summary>How many of a given item's CATEGORY are being carried.</summary>
        public int CountOfCategory(string category)
        {
            int n = 0;
            for (int i = 0; i < SlotCount; i++)
                if (!string.IsNullOrEmpty(slots[i]) && ItemCatalog.Get(slots[i]).Category == category) n++;
            return n;
        }

        /// <summary>Why an item cannot be taken, or null if it can. Shown in the interaction prompt.</summary>
        public string RejectReason(string itemId)
        {
            var def = ItemCatalog.Get(itemId);
            if (CountOfCategory(def.Category) >= def.MaxCount)
                return def.MaxCount == 1
                    ? $"already carrying a {def.Category}"
                    : $"already carrying {def.MaxCount} {def.Category}s";
            if (BulkUsed + def.Bulk > ItemCatalog.BagCapacity + 0.001f) return "bag is full";
            if (IsFull) return "no free hands";
            return null;
        }

        public bool CanAccept(string itemId) => RejectReason(itemId) == null;

        /// <summary>
        /// Puts the selected item back into the world at the player's feet. Without this, capacity
        /// would be a dead end rather than a decision — you could reach a door needing a third key
        /// with no way to swap anything out.
        /// </summary>
        public string DropSelected()
        {
            // Falls back to the first occupied slot. Only slots 0 and 1 are selectable by key, but
            // there are eight of them — without this, anything that landed in a later slot could
            // never be dropped at all and would silently block pickups for the rest of the run.
            int index = SelectedSlot;
            if (string.IsNullOrEmpty(slots[index]))
            {
                index = -1;
                for (int i = 0; i < SlotCount; i++)
                    if (!string.IsNullOrEmpty(slots[i])) { index = i; break; }
                if (index < 0) return null;
            }
            return TakeFromSlot(index);
        }

        /// <summary>
        /// Tools that wear out. A crowbar that never breaks makes every barred thing in the game a
        /// formality; two uses, and only two crowbars in the building, turns "which door do I spend
        /// this on" into a real question — and in co-op, into a conversation.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, int> MaxUses =
            new System.Collections.Generic.Dictionary<string, int> { { "crowbar", 2 } };

        private readonly System.Collections.Generic.Dictionary<string, int> usesSpent =
            new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>Uses left on a wearing tool, or -1 if it does not wear out.</summary>
        public int UsesLeft(string itemId)
        {
            if (itemId == null || !MaxUses.TryGetValue(itemId, out int max)) return -1;
            usesSpent.TryGetValue(itemId, out int spent);
            return Mathf.Max(0, max - spent);
        }

        /// <summary>
        /// Records one use of a wearing tool, removing it once it is spent. Callers that consume an
        /// item outright should keep using RemoveItem; this is for tools that survive a few jobs.
        /// </summary>
        public void RegisterUse(string itemId)
        {
            if (!MaxUses.ContainsKey(itemId)) return;
            usesSpent.TryGetValue(itemId, out int spent);
            usesSpent[itemId] = spent + 1;
            if (UsesLeft(itemId) <= 0)
            {
                usesSpent.Remove(itemId);   // so a fresh one picked up later starts clean
                RemoveItem(itemId);
            }
            else InventoryChanged?.Invoke();
        }

        /// <summary>Drops one specific slot. What the inventory panel's per-item buttons call.</summary>
        public string DropSlot(int index)
        {
            string id = TakeFromSlot(index);
            if (id == null) return null;

            Vector3 at = transform.position + transform.forward * 0.7f + Vector3.up * 0.15f;
            var net = GetComponent<LastWard.Net.PlayerNetworkState>();
            if (net != null) net.RequestDrop(id, at);
            return id;
        }

        private string TakeFromSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return null;
            string id = slots[index];
            if (string.IsNullOrEmpty(id)) return null;
            slots[index] = null;
            InventoryChanged?.Invoke();
            return id;
        }

        /// <summary>Every carried id, for the inventory panel. Nulls included so slots stay stable.</summary>
        public string GetSlotAt(int index) =>
            index >= 0 && index < SlotCount ? slots[index] : null;
        private readonly string[] slots = new string[SlotCount];

        [SerializeField] private PlayerInputReader input;

        public int SelectedSlot { get; private set; }
        public bool IsFull => Array.TrueForAll(slots, s => !string.IsNullOrEmpty(s));

        public event Action InventoryChanged;

        // Local set in OnEnable (not Awake) so only the owner's enabled inventory claims it — see
        // the same pattern in PlayerInputReader.
        private void OnEnable()
        {
            Local = this;
            input.InventorySlot1Pressed += OnSlot1;
            input.InventorySlot2Pressed += OnSlot2;
        }

        private void OnDisable()
        {
            if (Local == this) Local = null;
            input.InventorySlot1Pressed -= OnSlot1;
            input.InventorySlot2Pressed -= OnSlot2;
        }

        public bool TryAddItem(string itemId)
        {
            if (!CanAccept(itemId)) return false;
            for (int i = 0; i < SlotCount; i++)
            {
                if (string.IsNullOrEmpty(slots[i]))
                {
                    slots[i] = itemId;
                    InventoryChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        public string GetSlot(int index) => slots[index];

        public bool HasItem(string itemId)
        {
            for (int i = 0; i < SlotCount; i++)
                if (slots[i] == itemId) return true;
            return false;
        }

        public bool RemoveItem(string itemId)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (slots[i] == itemId)
                {
                    slots[i] = null;
                    InventoryChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        private void Update()
        {
            // Bound directly rather than through the input asset, same as the breath hold — the
            // .inputactions file is a binary blob this build pipeline does not regenerate.
            if (Local != this) return;
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) TryDropSelected();
        }

        /// <summary>
        /// Puts the selected item back into the world at your feet. Without a drop, capacity would be
        /// a dead end rather than a decision: you could reach the third lock holding two keys with no
        /// way to swap one out.
        /// </summary>
        public void TryDropSelected()
        {
            string id = DropSelected();
            if (id == null) return;

            Vector3 at = transform.position + transform.forward * 0.7f + Vector3.up * 0.15f;
            var net = GetComponent<LastWard.Net.PlayerNetworkState>();
            if (net != null) net.RequestDrop(id, at);
        }

        private void OnSlot1() => SelectSlot(0);
        private void OnSlot2() => SelectSlot(1);

        private void SelectSlot(int index)
        {
            SelectedSlot = index;
            InventoryChanged?.Invoke();
        }
    }
}
