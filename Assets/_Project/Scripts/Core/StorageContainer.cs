using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// A cupboard, locker or crate with something inside. Interact to open it; some need a key,
    /// some need a crowbar to lever apart.
    ///
    /// The contents are never spawned or despawned — they sit inside the shell from the start, and
    /// opening simply swings the door out of the way. That matters for two reasons: NetworkObjects
    /// that begin life inactive don't spawn cleanly under NGO, and with the door shut the pickups
    /// are already unreachable because the interaction raycast hits the door panel first. So
    /// "hidden" is enforced by geometry rather than by bookkeeping that could desync.
    /// </summary>
    public class StorageContainer : NetworkBehaviour, IInteractable
    {
        [Tooltip("Panel that swings aside when opened.")]
        [SerializeField] private Transform door;
        [Tooltip("Item id needed to open. Empty means it just opens.")]
        [SerializeField] private string requiredItemId = "";
        [Tooltip("Whether the required item is used up. Keys are; a crowbar isn't.")]
        [SerializeField] private bool consumesItem;
        [SerializeField] private string openPrompt = "Open";
        [SerializeField] private string lockedPrompt = "Locked";
        [SerializeField] private float openAngle = 105f;
        [SerializeField] private float openSpeed = 3.5f;

        private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);

        private Quaternion closedRotation;
        private Quaternion openedRotation;

        private void Awake()
        {
            if (door == null) return;
            closedRotation = door.localRotation;
            openedRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
        }

        private void Update()
        {
            if (door == null) return;
            var target = isOpen.Value ? openedRotation : closedRotation;
            door.localRotation = Quaternion.Slerp(door.localRotation, target, Time.deltaTime * openSpeed);
        }

        public string GetPrompt()
        {
            if (isOpen.Value) return null;
            if (string.IsNullOrEmpty(requiredItemId)) return openPrompt;

            // Names what's missing rather than a bare "Locked", so a locked container reads as a
            // lead to follow instead of a dead end.
            bool hasItem = PlayerInventory.Local != null && PlayerInventory.Local.HasItem(requiredItemId);
            return hasItem ? $"{openPrompt} (use {requiredItemId})" : $"{lockedPrompt} — needs {requiredItemId}";
        }

        public bool CanInteract(ulong playerId)
        {
            if (isOpen.Value) return false;
            if (string.IsNullOrEmpty(requiredItemId)) return true;
            return PlayerInventory.Local != null && PlayerInventory.Local.HasItem(requiredItemId);
        }

        public void Interact(ulong playerId)
        {
            if (isOpen.Value) return;

            // Checked locally because the inventory only exists on the owning client. The server
            // still owns the open state itself, so a client can't force one open by other means.
            if (!string.IsNullOrEmpty(requiredItemId))
            {
                if (PlayerInventory.Local == null || !PlayerInventory.Local.HasItem(requiredItemId)) return;
                if (consumesItem) PlayerInventory.Local.RemoveItem(requiredItemId);
            }

            GameEvents.RaiseNoiseEmitted(transform.position, 9f, NoiseSource.PuzzleInteraction);
            if (IsServer) isOpen.Value = true;
            else OpenServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void OpenServerRpc() => isOpen.Value = true;
    }
}
