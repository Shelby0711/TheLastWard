using LastWard.Net;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// A wardrobe, or the gap under a bed — somewhere to break line of sight and let the discovery
    /// meter drain. Interact to climb in; interact again to climb out.
    ///
    /// Server-authoritative because the Entity's senses read <see cref="PlayerNetworkState.IsHidden"/>:
    /// if a client set that locally, hiding would behave differently for the host than for everyone
    /// else. One occupant per spot, tracked in a NetworkVariable so a late joiner doesn't read an
    /// occupied wardrobe as free.
    ///
    /// Hiding is not a free escape: an Entity that watched you climb in still hunts your last known
    /// position, and the meter drains rather than resetting. It buys time, not safety.
    /// </summary>
    public class HidingSpot : NetworkBehaviour, IInteractable
    {
        [Tooltip("Where the player is placed while hidden. Defaults to this object's own position.")]
        [SerializeField] private Transform hidePoint;
        [SerializeField] private string enterPrompt = "Hide";
        [SerializeField] private string exitPrompt = "Come out";

        // ulong.MaxValue == empty.
        private readonly NetworkVariable<ulong> occupant =
            new NetworkVariable<ulong>(ulong.MaxValue);

        private Vector3 entryPosition;

        /// <summary>
        /// The spot the local player is currently inside, if any. Exists because the interaction
        /// raycast is useless from in here — it starts inside the very collider it would need to
        /// hit — so <see cref="PlayerInteractor"/> reads this to route the interact key to "get out"
        /// instead of firing a ray that can never connect. Without it there is literally no way out.
        /// </summary>
        public static HidingSpot LocalOccupied { get; private set; }

        public string ExitPrompt => exitPrompt;
        public bool IsOccupied => occupant.Value != ulong.MaxValue;

        private Vector3 HidePosition => hidePoint != null ? hidePoint.position : transform.position;

        public override void OnDestroy()
        {
            if (LocalOccupied == this) LocalOccupied = null;
            base.OnDestroy();
        }

        public string GetPrompt() => IsOccupied ? null : enterPrompt;

        public bool CanInteract(ulong playerId) => !IsOccupied || occupant.Value == playerId;

        public void Interact(ulong playerId)
        {
            if (IsServer) ServerToggle(playerId);
            else ToggleServerRpc(playerId);
        }

        /// <summary>Called by the local player's interact key while hidden — the only way out.</summary>
        public void RequestExit()
        {
            if (NetworkManager.Singleton == null) return;
            Interact(NetworkManager.Singleton.LocalClientId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ToggleServerRpc(ulong playerId) => ServerToggle(playerId);

        private void ServerToggle(ulong playerId)
        {
            if (IsOccupied && occupant.Value != playerId) return;   // someone else is in here
            if (IsOccupied) ServerRelease(playerId);
            else ServerEnter(playerId);
        }

        private void ServerEnter(ulong playerId)
        {
            var player = FindPlayer(playerId);
            if (player == null) return;
            // A dead player has no business climbing into a wardrobe.
            if (player.TryGetComponent<PlayerNetworkState>(out var state) && !state.IsAlive) return;

            occupant.Value = playerId;
            entryPosition = player.transform.position;
            state?.ServerSetHidden(true);
            SetHiddenClientRpc(playerId, true, HidePosition);
        }

        private void ServerRelease(ulong playerId)
        {
            var player = FindPlayer(playerId);
            occupant.Value = ulong.MaxValue;
            if (player != null && player.TryGetComponent<PlayerNetworkState>(out var state))
                state.ServerSetHidden(false);
            // Falls back to the spot's own position if we never recorded an entry point (host
            // migration, or a spot that was already occupied when this instance spawned).
            Vector3 exitTo = entryPosition == Vector3.zero ? ExitFallbackPosition() : entryPosition;
            SetHiddenClientRpc(playerId, false, exitTo);
        }

        private Vector3 ExitFallbackPosition() => transform.position + transform.forward * 1.2f;

        /// <summary>
        /// Called by the server when the occupant dies or disconnects, so a spot can't be left
        /// locked by someone who is no longer in it.
        /// </summary>
        public void ServerForceRelease()
        {
            if (IsServer && IsOccupied) ServerRelease(occupant.Value);
        }

        [ClientRpc]
        private void SetHiddenClientRpc(ulong playerId, bool hiding, Vector3 position)
        {
            if (NetworkManager.Singleton == null) return;
            if (NetworkManager.Singleton.LocalClientId != playerId) return;

            var player = FindPlayer(playerId);
            if (player == null) return;

            LocalOccupied = hiding ? this : null;

            // The CharacterController stays OFF for the whole time we're hidden, not just for the
            // teleport frame. It's what would otherwise fight being placed inside the wardrobe's
            // panels (or under a bed, below floor level) and shove the player back out.
            if (player.TryGetComponent<CharacterController>(out var controller))
            {
                controller.enabled = false;
                player.transform.position = position;
                if (!hiding) controller.enabled = true;
            }
            else
            {
                player.transform.position = position;
            }

            // Movement is frozen while hidden. Look is deliberately left alone so you can watch the
            // room through the slats and pick your moment to come out.
            if (player.TryGetComponent<FirstPersonMotor>(out var motor)) motor.enabled = !hiding;
        }

        private static GameObject FindPlayer(ulong playerId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return null;
            if (!nm.ConnectedClients.TryGetValue(playerId, out var client)) return null;
            return client.PlayerObject != null ? client.PlayerObject.gameObject : null;
        }
    }
}
