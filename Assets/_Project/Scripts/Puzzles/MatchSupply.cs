using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The run's matches. Ten of them, for everybody, for the whole game.
    ///
    /// Held here rather than on the player because the box is a single physical object that gets
    /// passed around, dropped and argued over — and the inventory is id-based, so an item cannot
    /// carry a charge count with it from one bag to another. A team-wide pool sidesteps that entirely
    /// and happens to be exactly what the design wants: one box, one economy, one set of decisions.
    ///
    /// Server-authoritative. A client asking to spend a match that is not there is simply refused.
    /// </summary>
    public class MatchSupply : NetworkBehaviour
    {
        public static MatchSupply Instance { get; private set; }

        [Tooltip("Ten matches against ten candle positions on the floor: the player can light the " +
            "whole route exactly once, or ration it. The economy and the level length agree by " +
            "construction — do not change one without the other.")]
        [SerializeField] private int startingMatches = 10;

        private readonly NetworkVariable<int> remaining = new NetworkVariable<int>();

        public int Remaining => remaining.Value;
        public bool Any => remaining.Value > 0;

        private bool issued;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            remaining.Value = startingMatches;
            StartCoroutine(IssueBox());
        }

        /// <summary>
        /// Hands the box to exactly one player at the start of the run. It is never found in the
        /// building — that is the point. Somebody drops it on floor one for an extra battery, because
        /// on floor one it is obviously useless, and gets shouted at about it two floors later.
        ///
        /// Random rather than always the host, so the same person is not the designated carrier every
        /// run and the group has to work out who has it.
        /// </summary>
        private System.Collections.IEnumerator IssueBox()
        {
            var nm = NetworkManager.Singleton;
            while (nm == null || nm.ConnectedClientsList.Count == 0) yield return null;
            // Long enough for latecomers to be in the lobby and for PlayerInventory to exist client
            // side. There is no explicit run-start event to hang this on yet.
            yield return new WaitForSeconds(3f);
            if (issued) yield break;
            issued = true;

            var list = nm.ConnectedClientsList;
            ulong who = list[Random.Range(0, list.Count)].ClientId;
            GiveClientRpc(new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { who } }
            });
            Debug.Log($"[MatchSupply] Box of matches issued to client {who} ({startingMatches} matches).");
        }

        [ClientRpc]
        private void GiveClientRpc(ClientRpcParams p = default)
        {
            // Silently. No prompt, no popup, no flag — if the game announces it, nobody drops it and
            // the trap never springs.
            LastWard.Player.PlayerInventory.Local?.TryAddItem("matchbox");
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Server-only. Takes one match if there is one; false if the box is empty.</summary>
        public bool ServerSpend()
        {
            if (!IsServer || remaining.Value <= 0) return false;
            remaining.Value -= 1;
            return true;
        }
    }
}
