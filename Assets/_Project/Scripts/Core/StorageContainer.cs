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
    /// opening simply swings the door out of the way. NetworkObjects that begin life inactive don't
    /// spawn cleanly under NGO, so the shell keeps them.
    ///
    /// Reachability, though, is enforced EXPLICITLY: while the door is shut every interactable inside
    /// has its collider disabled. Relying on the door panel to block the interaction raycast was the
    /// original plan and it did not hold — players could read a note straight through a closed
    /// cupboard, because the ray found the note's collider through a seam in the shell. This is
    /// driven entirely off the replicated open flag, so there is no state to desync.
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
        // CUMULATIVE. Physics.OverlapBox only reports ENABLED colliders, so once a content collider
        // is switched off it becomes invisible to the next scan. Rebuilding this list per scan
        // therefore emptied it and the contents could never be switched back on — every fuse, key
        // and note sealed in a container became permanently unpickable. Never drop a known content.
        private readonly System.Collections.Generic.HashSet<Collider> contents =
            new System.Collections.Generic.HashSet<Collider>();
        private float nextContentScan;
        private bool applied;
        private bool appliedOpen;

        private void Awake()
        {
            if (door == null) return;
            closedRotation = door.localRotation;
            openedRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
        }

        private void Update()
        {
            // Contents are rescanned for a while after spawn: ClueSpawnShuffler moves notes and
            // pickups into place a frame or two AFTER everything wakes up, so a single scan in Start
            // would miss whatever landed here.
            if (Time.timeSinceLevelLoad < 8f && Time.time >= nextContentScan)
            {
                nextContentScan = Time.time + 0.5f;
                ScanForContents();
            }
            ApplyContentReachability(isOpen.Value);

            if (door == null) return;
            var target = isOpen.Value ? openedRotation : closedRotation;
            door.localRotation = Quaternion.Slerp(door.localRotation, target, Time.deltaTime * openSpeed);
        }

        /// <summary>
        /// Interactables physically inside this shell. Found by overlapping the shell's own bounds
        /// rather than by a hand-authored list, so it keeps working when the shuffler relocates items.
        /// </summary>
        /// <summary>
        /// Adds any interactable sitting inside the shell to <see cref="contents"/>. Everything known
        /// is switched back ON for the duration of the query, because a disabled collider cannot be
        /// found by an overlap test — without that, the very items already being hidden would drop
        /// out of the list and never be restored.
        /// </summary>
        private void ScanForContents()
        {
            foreach (var c in contents)
                if (c != null) c.enabled = true;

            var shell = GetComponentInChildren<Collider>();
            if (shell != null)
            {
                Bounds b = shell.bounds;
                foreach (var c in GetComponentsInChildren<Collider>()) b.Encapsulate(c.bounds);

                foreach (var hit in Physics.OverlapBox(b.center, b.extents * 0.9f, Quaternion.identity,
                             ~0, QueryTriggerInteraction.Collide))
                {
                    if (hit == null) continue;
                    if (hit.transform.IsChildOf(transform)) continue;               // the shell and its door
                    if (hit.GetComponentInParent<StorageContainer>() != null) continue;
                    if (hit.GetComponentInParent<IInteractable>() == null) continue; // only things you use
                    // Must actually be INSIDE, not merely brushing the shell's bounding box. A note
                    // resting against the outside of a cupboard would otherwise be sealed away by a
                    // container it was never in.
                    if (!b.Contains(hit.bounds.center)) continue;
                    contents.Add(hit);
                }
            }

            applied = false;   // re-apply the correct state now the list has changed
        }

        private void ApplyContentReachability(bool open)
        {
            if (applied && appliedOpen == open) return;
            foreach (var c in contents)
                if (c != null) c.enabled = open;
            applied = true;
            appliedOpen = open;
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
                else PlayerInventory.Local.RegisterUse(requiredItemId);   // tools wear out instead
            }

            GameEvents.RaiseNoiseEmitted(transform.position, 9f, NoiseSource.PuzzleInteraction);
            if (IsServer) isOpen.Value = true;
            else OpenServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void OpenServerRpc() => isOpen.Value = true;
    }
}
