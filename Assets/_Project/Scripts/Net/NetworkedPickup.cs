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

        /// <summary>
        /// Every pickup in the scene, so a dropped item can find its own object again and put itself
        /// back. Dropping spawns nothing new — it un-takes the original, which keeps the item count
        /// in the level fixed and avoids needing a networked prefab for every carryable.
        /// </summary>
        private static readonly System.Collections.Generic.List<NetworkedPickup> All =
            new System.Collections.Generic.List<NetworkedPickup>();

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        /// <summary>Server-only. Puts a carried item back on the floor at <paramref name="at"/>.</summary>
        public static bool ServerDropItem(string itemId, Vector3 at)
        {
            foreach (var p in All)
            {
                if (p == null || !p.taken.Value || p.itemId != itemId) continue;
                p.transform.position = at;
                p.taken.Value = false;
                return true;
            }
            return false;
        }

        public string GetPrompt()
        {
            if (taken.Value) return null;
            // Without this the prompt still reads "Pick up X" while CanInteract quietly refuses,
            // so the key looks broken rather than the hands looking full.
            var reason = PlayerInventory.Local?.RejectReason(itemId);
            if (reason != null) return $"{displayName} — {reason}";
            return $"Pick up {displayName}";
        }

        public bool CanInteract(ulong playerId) =>
            !taken.Value && PlayerInventory.Local != null && PlayerInventory.Local.CanAccept(itemId);

        public void Interact(ulong playerId)
        {
            // A battery is used the instant it is picked up rather than carried. Storing it would
            // eat one of only four slots for something with exactly one use.
            if (itemId == "battery")
            {
                var cell = LastWard.Player.FlashlightBattery.Local;
                if (cell == null || !cell.AddBattery()) return;
                RequestTakeServerRpc();
                return;
            }

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
