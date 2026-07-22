using LastWard.Core;
using LastWard.Player;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>Thin IInteractable wrapper: consumes a held fuse locally, tells the puzzle
    /// controller, and colors itself green once filled so it's obvious at a glance.</summary>
    public class FuseSocket : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color EmptyColor = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color FilledColor = new Color(0.05f, 0.45f, 0.1f);

        [SerializeField] private FusePowerPuzzle puzzle;
        [SerializeField] private int slotIndex;
        [SerializeField] private string requiredItemId = "fuse";

        private Renderer rend;
        private MaterialPropertyBlock mpb;
        private bool lastInserted;

        private void Awake()
        {
            rend = GetComponentInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
        }

        // Polled rather than event-driven: only 2 sockets exist, and the puzzle doesn't otherwise
        // need a per-slot event just for this cosmetic check.
        private void Update()
        {
            if (puzzle == null) return;
            bool inserted = puzzle.IsFuseInserted(slotIndex);
            if (inserted == lastInserted) return;
            lastInserted = inserted;
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, inserted ? FilledColor : EmptyColor);
            rend.SetPropertyBlock(mpb);
        }

        public string GetPrompt() =>
            puzzle != null && puzzle.IsFuseInserted(slotIndex) ? "Fuse inserted" : $"Insert {requiredItemId}";

        public bool CanInteract(ulong playerId) =>
            puzzle != null && !puzzle.IsFuseInserted(slotIndex) &&
            PlayerInventory.Local != null && PlayerInventory.Local.HasItem(requiredItemId);

        public void Interact(ulong playerId)
        {
            if (PlayerInventory.Local == null || !PlayerInventory.Local.RemoveItem(requiredItemId)) return;
            puzzle.RequestInsertFuseServerRpc(slotIndex);
        }
    }
}
