using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Net
{
    /// <summary>
    /// Networked counterpart to LastWard.Player.Pickup — server owns the taken flag so the item
    /// disappears for everyone. Prototype limitation: the local inventory add happens optimistically
    /// before the server confirms, so two players grabbing in the same frame could both bank it.
    /// Fine at 4-player co-op scale; tighten to a server-confirmed grant if it ever matters.
    /// </summary>
    public class NetworkedPickup : NetworkBehaviour, IInteractable
    {
        [SerializeField] private string itemId = "item";
        [SerializeField] private string displayName = "item";

        private readonly NetworkVariable<bool> taken = new NetworkVariable<bool>();

        public override void OnNetworkSpawn()
        {
            taken.OnValueChanged += OnTakenChanged;
            ApplyTaken(taken.Value);
        }

        public override void OnNetworkDespawn()
        {
            taken.OnValueChanged -= OnTakenChanged;
        }

        private void OnTakenChanged(bool previous, bool current) => ApplyTaken(current);

        private void ApplyTaken(bool isTaken)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = !isTaken;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = !isTaken;
        }

        public string GetPrompt()
        {
            if (taken.Value) return null;
            // Without this the prompt still reads "Pick up X" while CanInteract quietly refuses,
            // so the key looks broken rather than the hands looking full.
            if (PlayerInventory.Local != null && PlayerInventory.Local.IsFull)
                return $"Hands full — can't take {displayName}";
            return $"Pick up {displayName}";
        }

        public bool CanInteract(ulong playerId) =>
            !taken.Value && PlayerInventory.Local != null && !PlayerInventory.Local.IsFull;

        public void Interact(ulong playerId)
        {
            if (PlayerInventory.Local != null && PlayerInventory.Local.TryAddItem(itemId))
                RequestTakeServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestTakeServerRpc()
        {
            if (!taken.Value) taken.Value = true;
        }
    }
}
