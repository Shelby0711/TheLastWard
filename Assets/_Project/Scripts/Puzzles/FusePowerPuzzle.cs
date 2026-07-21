using System;
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
        // Bitmask of breakers correctly flipped so far this attempt — lets each BreakerSwitch know
        // to show itself as green without tracking the sequence itself.
        private readonly NetworkVariable<int> correctMask = new NetworkVariable<int>(0);
        // One-shot signal: which breaker was just pressed wrong, so that switch alone can flash red.
        // -1 = no pending flash. Clients react to the value CHANGING, so two different wrong presses
        // in a row both fire even though the mask resets to 0 either way.
        private readonly NetworkVariable<int> lastWrongBreaker = new NetworkVariable<int>(-1);

        public bool IsPowered => fuseAInserted.Value && fuseBInserted.Value;
        public bool IsSolved => solved.Value;
        public bool IsFuseInserted(int slot) => slot == 0 ? fuseAInserted.Value : fuseBInserted.Value;
        public int CorrectMask => correctMask.Value;

        /// <summary>Fires on every client with the updated correct-so-far bitmask.</summary>
        public event Action<int> CorrectMaskChanged;
        /// <summary>Fires on every client with the breaker index that was just pressed wrong.</summary>
        public event Action<int> WrongBreakerPressed;

        public override void OnNetworkSpawn()
        {
            correctMask.OnValueChanged += (_, now) => CorrectMaskChanged?.Invoke(now);
            lastWrongBreaker.OnValueChanged += (_, now) => { if (now >= 0) WrongBreakerPressed?.Invoke(now); };
        }

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
                correctMask.Value = 0;
                lastWrongBreaker.Value = breakerIndex;
                return;
            }

            breakerProgress.Value++;
            correctMask.Value |= 1 << breakerIndex;
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
