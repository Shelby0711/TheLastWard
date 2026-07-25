using System.Collections.Generic;

namespace LastWard.Player
{
    /// <summary>What one carryable thing costs you to carry.</summary>
    public readonly struct ItemDef
    {
        /// <summary>Share of the bag it fills, 0..1. A crowbar is not a key.</summary>
        public readonly float Bulk;
        /// <summary>How many of this KIND may be carried at once, regardless of space left.</summary>
        public readonly int MaxCount;
        /// <summary>Grouping for that limit, so all three keys share one allowance.</summary>
        public readonly string Category;
        public readonly string Display;

        public ItemDef(string category, float bulk, int maxCount, string display)
        {
            Category = category; Bulk = bulk; MaxCount = maxCount; Display = display;
        }
    }

    /// <summary>
    /// The carrying rules, in one place.
    ///
    /// Two limits act at once and they do different jobs. <b>Bulk</b> is a soft budget — everything
    /// competes for the same bag, so taking the crowbar genuinely costs you room for something else.
    /// <b>Category caps</b> are hard walls that exist for co-op: no one player may hold all three
    /// corridor keys, so either you make two trips or somebody else carries one. That is the whole
    /// reason the corridor door has three locks.
    ///
    /// Unknown ids fall back to a small generic entry rather than throwing, so adding a pickup never
    /// breaks the bag before its rules are written.
    /// </summary>
    public static class ItemCatalog
    {
        public const float BagCapacity = 1f;

        private static readonly ItemDef Fallback = new ItemDef("misc", 0.15f, 4, "item");

        private static readonly Dictionary<string, ItemDef> Table = new Dictionary<string, ItemDef>
        {
            // Keys: small, but hard-capped at two. The three-lock door is built around this.
            { "lock_key_top",    new ItemDef("key", 0.10f, 2, "brass key") },
            { "lock_key_middle", new ItemDef("key", 0.10f, 2, "steel key") },
            { "lock_key_bottom", new ItemDef("key", 0.10f, 2, "iron key") },
            { "key",             new ItemDef("key", 0.10f, 2, "rusted key") },

            // Weapons: one only. Two would make the throw a repeatable answer to the Entity.
            { "pipe",  new ItemDef("weapon", 0.30f, 1, "pipe") },
            { "knife", new ItemDef("weapon", 0.20f, 1, "knife") },

            // Tools: bulky, and the crowbar is the single most expensive thing to carry.
            { "crowbar", new ItemDef("tool", 0.35f, 1, "crowbar") },

            // Consumables.
            { "battery", new ItemDef("battery", 0.12f, 2, "flashlight battery") },
            // The generator cell is deliberately enormous: over half the bag, one only. Carrying it
            // means carrying almost nothing else, which is the co-op split the gate is built to force.
            { "cell",    new ItemDef("cell",    0.55f, 1, "heavy cell") },
            { "fuse",    new ItemDef("fuse",    0.15f, 2, "fuse") },
        };

        public static ItemDef Get(string itemId) =>
            itemId != null && Table.TryGetValue(itemId, out var def) ? def : Fallback;

        public static string DisplayName(string itemId) => Get(itemId).Display;
    }
}
