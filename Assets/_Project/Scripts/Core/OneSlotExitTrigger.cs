using LastWard.Net;
using LastWard.UI;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// The bible's endgame twist, minimally implemented: the exit door only lets one player through.
    /// The first to cross this trigger (placed just beyond the door) seals it immediately via
    /// ServerSetLocked, so no one else can follow — "no one leaves cleanly" for the rest by default.
    /// Broadcasts the outcome to every client so the shared farewell beat plays for everyone, not
    /// just the escapee.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class OneSlotExitTrigger : NetworkBehaviour
    {
        [SerializeField] private NetworkedDoor exitDoor;

        private bool sealedAlready;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || sealedAlready) return;
            if (other.GetComponentInParent<NetworkPlayer>() == null) return;
            var playerNetObj = other.GetComponentInParent<NetworkObject>();
            if (playerNetObj == null) return;

            sealedAlready = true;
            if (exitDoor != null) exitDoor.ServerSetLocked(true);
            ObjectiveTracker.Instance?.ServerAdvanceTo(ObjectiveStage.Ended);

            AnnounceEndingClientRpc(playerNetObj.OwnerClientId);
        }

        [ClientRpc]
        private void AnnounceEndingClientRpc(ulong escapedClientId)
        {
            bool isEscapee = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == escapedClientId;
            EndingUI.Instance?.Show(isEscapee);
        }
    }
}
