using LastWard.Core;
using LastWard.Entity;
using LastWard.UI;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// The pipe: one swing, then it's gone. Connecting drives the Entity off for a while, and the
    /// pipe goes with it — so carrying one is a single "not yet" you can spend, never a way to
    /// fight. That's deliberate: a weapon you can reuse turns the Entity into an enemy to be
    /// managed, which is the opposite of what should happen when it finds you.
    ///
    /// Server-authoritative: the client asks, the server re-checks range and applies the repel, so a
    /// client can't drive the Entity off from across the map.
    /// </summary>
    public class PlayerMeleeDefense : NetworkBehaviour
    {
        public static PlayerMeleeDefense Local { get; private set; }

        [SerializeField] private Camera swingCamera;
        [SerializeField] private string weaponItemId = "pipe";
        [Tooltip("How close the Entity must be for a swing to land.")]
        [SerializeField] private float swingRange = 3f;
        [Tooltip("Cone in front of the player a swing can connect within.")]
        [SerializeField, Range(0f, 180f)] private float swingHalfAngle = 65f;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        public bool HasWeapon => PlayerInventory.Local != null && PlayerInventory.Local.HasItem(weaponItemId);

        /// <summary>
        /// Attempts a swing. Returns true if one was made, so the caller knows the input was
        /// consumed and shouldn't also fire a normal interaction.
        /// </summary>
        public bool TryStrike()
        {
            if (!IsOwner || !HasWeapon) return false;

            var entity = FindObjectOfType<EntityController>();
            if (entity == null) return false;

            Vector3 toEntity = entity.transform.position - transform.position;
            if (toEntity.magnitude > swingRange) return false;

            Vector3 facing = swingCamera != null ? swingCamera.transform.forward : transform.forward;
            facing.y = 0f;
            Vector3 flat = toEntity;
            flat.y = 0f;
            if (Vector3.Angle(facing, flat) > swingHalfAngle) return false;

            // Spent locally the moment it connects, so a laggy confirmation can't let the same pipe
            // be swung twice.
            PlayerInventory.Local.RemoveItem(weaponItemId);
            InteractionPromptUI.Instance?.SetPrompt(null);
            GameEvents.RaiseNoiseEmitted(transform.position, 14f, NoiseSource.PuzzleInteraction);
            StrikeServerRpc();
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void StrikeServerRpc()
        {
            var entity = FindObjectOfType<EntityController>();
            if (entity == null) return;
            // Re-checked on the server; the client's claim isn't trusted.
            if (Vector3.Distance(entity.transform.position, transform.position) > swingRange * 1.5f) return;
            entity.ServerRepel(transform.position);
        }
    }
}
