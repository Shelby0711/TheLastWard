using System.Collections.Generic;
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

        // Every point claimed by ANY shuffler this run. Several shufflers deliberately share the
        // same candidate pool (the Lobby stashes feed the fuses, the tools and the notes), and each
        // one used to permute that pool independently — so two of them regularly landed a note and a
        // fuse on the exact same shelf. Claims are global so a point is only ever used once.
        private static readonly HashSet<Transform> ClaimedPoints = new HashSet<Transform>();
        private static float claimEpoch = -1f;

        private void ServerShuffle()
        {
            // First shuffler of a run wipes the register (a rebuilt scene or a restart must not
            // inherit last run's claims).
            if (!Mathf.Approximately(claimEpoch, Time.timeSinceLevelLoad))
            {
                if (claimEpoch < 0f || Time.timeSinceLevelLoad < claimEpoch) ClaimedPoints.Clear();
                claimEpoch = Time.timeSinceLevelLoad;
            }

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

            // Walk the shuffled order and take the first UNCLAIMED point for each clue, so two
            // shufflers sharing a pool cannot stack their items on one spot.
            var positions = new Vector3[clues.Length];
            int cursor = 0;
            for (int i = 0; i < clues.Length; i++)
            {
                Transform chosen = null;
                while (cursor < n)
                {
                    var candidate = candidatePoints[indices[cursor++]];
                    if (candidate == null || ClaimedPoints.Contains(candidate)) continue;
                    chosen = candidate;
                    break;
                }
                // Pool exhausted (more clues than free points): fall back to any point rather than
                // leaving the clue at the origin, and warn — that is a level-authoring problem.
                if (chosen == null)
                {
                    chosen = candidatePoints[indices[i % n]];
                    Debug.LogWarning($"[Shuffler] {name}: ran out of unclaimed spawn points; " +
                        "an item may share a spot. Add more candidate points.");
                }
                ClaimedPoints.Add(chosen);
                positions[i] = chosen.position;
            }

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
