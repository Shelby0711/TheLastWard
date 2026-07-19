using LastWard.Core;
using UnityEngine;

namespace LastWard.Player
{
    public class Pickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName = "item";

        public string GetPrompt() => $"Pick up {displayName}";
        public bool CanInteract(ulong playerId) => PlayerInventory.Local != null && !PlayerInventory.Local.IsFull;

        public void Interact(ulong playerId)
        {
            if (PlayerInventory.Local != null && PlayerInventory.Local.TryAddItem(itemId))
            {
                gameObject.SetActive(false);
            }
        }
    }
}
