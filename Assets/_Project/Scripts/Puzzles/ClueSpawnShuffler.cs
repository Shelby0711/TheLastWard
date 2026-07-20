using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// Randomizes which of several candidate points each clue prop spawns at, so clue positions vary
    /// between playthroughs (PROTOTYPE_PLAN.md §7/§14 — "clue spawn positions shuffle among ~3 pool
    /// points each"). The clue props themselves don't need NetworkObjects: every client already has
    /// an identical copy of them from the shared scene, so only the one-time position choice needs
    /// to match everywhere — the server picks a permutation and broadcasts final positions once.
    /// Requires candidatePoints.Length >= clues.Length.
    /// </summary>
    public class ClueSpawnShuffler : NetworkBehaviour
    {
        [SerializeField] private Transform[] clues;
        [SerializeField] private Transform[] candidatePoints;

        public override void OnNetworkSpawn()
        {
            if (IsServer) ServerShuffle();
        }

        private void ServerShuffle()
        {
            if (candidatePoints.Length < clues.Length)
            {
                Debug.LogError("ClueSpawnShuffler needs at least as many candidate points as clues.");
                return;
            }

            int n = candidatePoints.Length;
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            var positions = new Vector3[clues.Length];
            for (int i = 0; i < clues.Length; i++)
                positions[i] = candidatePoints[indices[i]].position;

            ApplyPositionsClientRpc(positions);
        }

        [ClientRpc]
        private void ApplyPositionsClientRpc(Vector3[] positions)
        {
            for (int i = 0; i < clues.Length && i < positions.Length; i++)
                if (clues[i] != null) clues[i].position = positions[i];
        }
    }
}
