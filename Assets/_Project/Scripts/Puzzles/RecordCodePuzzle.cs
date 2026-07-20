using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Net;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// P2 from the plan: a keypad code derived from patient files, one of which is a deliberate
    /// contradiction (dated after the criterion note's cutoff) that a careful reading excludes.
    /// Same server-authoritative shape as FusePowerPuzzle: client requests, server validates and
    /// owns the solved state, completion unlocks a gated door + emits noise + awards knowledge.
    /// No lockout on wrong attempts — the noise on every submission is the deterrent per the plan
    /// ("brute force blocked by keypad noise + digit space"), not a hard block.
    /// </summary>
    public class RecordCodePuzzle : NetworkBehaviour
    {
        [SerializeField] private NetworkedDoor gatedDoor;
        [SerializeField] private string correctCode = "482";
        [SerializeField] private float knowledgeOnComplete = 5f;
        [SerializeField] private float noiseRadius = 6f;

        private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>();

        public bool IsSolved => solved.Value;

        [ServerRpc(RequireOwnership = false)]
        public void RequestSubmitCodeServerRpc(string code, ServerRpcParams rpcParams = default)
        {
            if (solved.Value) return;
            GameEvents.RaiseNoiseEmitted(transform.position, noiseRadius, NoiseSource.PuzzleInteraction);
            if (code != correctCode) return;

            solved.Value = true;
            ulong solverId = rpcParams.Receive.SenderClientId;
            if (gatedDoor != null) gatedDoor.ServerSetLocked(false);
            GameEvents.RaisePuzzleStepCompleted("p2_record_code", solverId);
            KnowledgeService.Instance?.AddScore(solverId, knowledgeOnComplete);
        }
    }
}
