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
        [Tooltip("Anything carried that can be swung. All of them behave the same: one hit, gone.")]
        [SerializeField] private string[] weaponItemIds = { "pipe", "knife" };
        [Tooltip("Furthest you can land a throw.")]
        [SerializeField] private float throwRange = 22f;
        [Tooltip("You cannot throw once it is this close. Weapons are something you spend to keep " +
            "it away, never something you use once it already has you - at that point the only " +
            "thing left is to have run sooner.")]
        [SerializeField] private float throwMinDistance = 6.5f;
        [Tooltip("How accurately you must be aiming at it. Tight enough that a panicked spin-and-" +
            "throw misses.")]
        [SerializeField, Range(0f, 180f)] private float throwHalfAngle = 22f;
        [Tooltip("Seconds it is stopped dead for. Long enough to break line of sight, not to escape.")]
        [SerializeField] private float stunSeconds = 4f;

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        /// <summary>The carried weapon, or null.</summary>
        public string HeldWeapon
        {
            get
            {
                if (PlayerInventory.Local == null) return null;
                foreach (var id in weaponItemIds)
                    if (PlayerInventory.Local.HasItem(id)) return id;
                return null;
            }
        }

        public bool HasWeapon => HeldWeapon != null;

        // Cached: there is exactly one Entity, and the old code re-scanned every object in the
        // scene on each swing using the deprecated FindObjectOfType.
        private EntityController cachedEntity;
        private EntityController Entity
        {
            get
            {
                if (cachedEntity == null) cachedEntity = FindAnyObjectByType<EntityController>();
                return cachedEntity;
            }
        }

        /// <summary>
        /// Throws the carried weapon at the Entity. Returns true if one was thrown, so the caller
        /// knows the input was consumed and shouldn't also fire a normal interaction.
        ///
        /// Deliberately a RANGED option with a dead zone: it only lands between throwMinDistance and
        /// throwRange. Swinging as it closed on you meant the weapon was a get-out-of-jail card at
        /// the exact moment the game should be unwinnable. Now it is something you spend early, from
        /// across a room, to buy distance you have not got yet.
        /// </summary>
        public bool TryThrow()
        {
            if (!IsOwner) return false;
            string weapon = HeldWeapon;
            if (weapon == null) return false;

            var entity = Entity;
            if (entity == null) return false;

            Vector3 toEntity = entity.transform.position - transform.position;
            float range = toEntity.magnitude;
            if (range > throwRange || range < throwMinDistance) return false;

            Vector3 facing = swingCamera != null ? swingCamera.transform.forward : transform.forward;
            facing.y = 0f;
            Vector3 flat = toEntity;
            flat.y = 0f;
            if (Vector3.Angle(facing, flat) > throwHalfAngle) return false;

            // Spent locally the moment it leaves your hand, so a laggy confirmation can't let the
            // same pipe be thrown twice.
            PlayerInventory.Local.RemoveItem(weapon);
            InteractionPromptUI.Instance?.SetPrompt(null);
            GameEvents.RaiseNoiseEmitted(transform.position, 14f, NoiseSource.PuzzleInteraction);
            ThrowServerRpc();
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ThrowServerRpc()
        {
            var entity = Entity;
            if (entity == null) return;
            // Re-checked on the server; the client's claim isn't trusted.
            float range = Vector3.Distance(entity.transform.position, transform.position);
            if (range > throwRange * 1.2f || range < throwMinDistance * 0.8f) return;
            entity.ServerStun(stunSeconds);
            ImpactClientRpc(entity.transform.position);
        }

        [ClientRpc]
        private void ImpactClientRpc(Vector3 at)
        {
            var clip = LastWard.Audio.GameSfx.ObjectFalling;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, at, 0.9f);
        }
    }
}
