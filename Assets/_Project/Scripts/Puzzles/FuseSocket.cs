using LastWard.Core;
using LastWard.Player;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>Thin IInteractable wrapper: consumes a held fuse locally, tells the puzzle controller.</summary>
    public class FuseSocket : MonoBehaviour, IInteractable
    {
        [SerializeField] private FusePowerPuzzle puzzle;
        [SerializeField] private int slotIndex;
        [SerializeField] private string requiredItemId = "fuse";

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
