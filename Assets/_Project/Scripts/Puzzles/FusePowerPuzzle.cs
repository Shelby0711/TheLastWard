using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Net;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// P1 from the plan: 2 fuses power the box, then a breaker sequence (order given by a note
    /// elsewhere in the room) unlocks a gated door. Server-authoritative — FuseSocket/BreakerSwitch
    /// are thin IInteractable wrappers that call these ServerRpcs. This is the reference "puzzle
    /// step" shape P2/P3 (M5 continuation) follow: client requests, server validates + owns state
    /// in NetworkVariables, completion emits noise + awards knowledge + unlocks a gate.
    /// </summary>
    public class FusePowerPuzzle : NetworkBehaviour
    {
        [SerializeField] private NetworkedDoor gatedDoor;
        [Tooltip("Correct breaker press order, as 0-based breaker indices.")]
        [SerializeField] private int[] correctBreakerOrder = { 1, 2, 0 };
        [SerializeField] private float knowledgeOnComplete = 5f;
        [SerializeField] private float noiseRadius = 10f;
        [Tooltip("Shared objective stage this puzzle advances the party to on solve. No-ops if no ObjectiveTracker exists in the scene (e.g. the M2 systems sandbox).")]
        [SerializeField] private ObjectiveStage advancesTo = ObjectiveStage.OrphanWard;

        private readonly NetworkVariable<bool> fuseAInserted = new NetworkVariable<bool>();
        private readonly NetworkVariable<bool> fuseBInserted = new NetworkVariable<bool>();
        private readonly NetworkVariable<int> breakerProgress = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>();

        public bool IsPowered => fuseAInserted.Value && fuseBInserted.Value;
        public bool IsSolved => solved.Value;
        public bool IsFuseInserted(int slot) => slot == 0 ? fuseAInserted.Value : fuseBInserted.Value;

        [ServerRpc(RequireOwnership = false)]
        public void RequestInsertFuseServerRpc(int slot)
        {
            if (solved.Value) return;
            if (slot == 0) fuseAInserted.Value = true; else fuseBInserted.Value = true;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.PuzzleInteraction);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestFlipBreakerServerRpc(int breakerIndex, ServerRpcParams rpcParams = default)
        {
            if (solved.Value || !IsPowered) return;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.PuzzleInteraction);

            int expected = correctBreakerOrder[breakerProgress.Value];
            if (breakerIndex != expected)
            {
                breakerProgress.Value = 0;
                return;
            }

            breakerProgress.Value++;
            if (breakerProgress.Value < correctBreakerOrder.Length) return;

            solved.Value = true;
            ulong solverId = rpcParams.Receive.SenderClientId;
            if (gatedDoor != null) gatedDoor.ServerSetLocked(false);
            GameEvents.RaisePuzzleStepCompleted("p1_fuse_power", solverId);
            KnowledgeService.Instance?.AddScore(solverId, knowledgeOnComplete);
            ObjectiveTracker.Instance?.ServerAdvanceTo(advancesTo);
        }
    }
}
