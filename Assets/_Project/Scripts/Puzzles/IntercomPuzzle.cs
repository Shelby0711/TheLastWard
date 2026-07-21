using System;
using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Net;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// P3 from the plan: 3 intercom stations activated in an order given by a radio/note elsewhere,
    /// spread apart so it favors one player relaying the order while others run stations. Same
    /// order-sequence shape as FusePowerPuzzle's breaker step — kept as its own small class rather
    /// than sharing a base with it, since the two puzzles' surrounding rules (fuses vs. none) differ
    /// enough that a shared abstraction would mostly be indirection.
    /// </summary>
    public class IntercomPuzzle : NetworkBehaviour
    {
        [SerializeField] private NetworkedDoor gatedDoor;
        [Tooltip("Correct activation order, as 0-based station indices.")]
        [SerializeField] private int[] correctOrder = { 2, 0, 1 };
        [SerializeField] private float knowledgeOnComplete = 5f;
        [SerializeField] private float noiseRadius = 14f;

        private readonly NetworkVariable<int> progress = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>();
        // Same feedback pattern as FusePowerPuzzle: bitmask of stations correct so far, plus a
        // one-shot "which station was just wrong" signal for a red flash.
        private readonly NetworkVariable<int> correctMask = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> lastWrongStation = new NetworkVariable<int>(-1);

        public bool IsSolved => solved.Value;
        public int CorrectMask => correctMask.Value;

        public event Action<int> CorrectMaskChanged;
        public event Action<int> WrongStationPressed;

        public override void OnNetworkSpawn()
        {
            correctMask.OnValueChanged += (_, now) => CorrectMaskChanged?.Invoke(now);
            lastWrongStation.OnValueChanged += (_, now) => { if (now >= 0) WrongStationPressed?.Invoke(now); };
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestActivateServerRpc(int stationIndex, ServerRpcParams rpcParams = default)
        {
            if (solved.Value) return;
            // Long-range noise — this is deliberately the loudest interaction in the puzzle set,
            // matching the plan's "every station activation attracts the Entity."
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.PuzzleInteraction);

            int expected = correctOrder[progress.Value];
            if (stationIndex != expected)
            {
                progress.Value = 0;
                correctMask.Value = 0;
                lastWrongStation.Value = stationIndex;
                return;
            }

            progress.Value++;
            correctMask.Value |= 1 << stationIndex;
            if (progress.Value < correctOrder.Length) return;

            solved.Value = true;
            ulong solverId = rpcParams.Receive.SenderClientId;
            if (gatedDoor != null) gatedDoor.ServerSetLocked(false);
            GameEvents.RaisePuzzleStepCompleted("p3_intercom", solverId);
            KnowledgeService.Instance?.AddScore(solverId, knowledgeOnComplete);
        }
    }
}
