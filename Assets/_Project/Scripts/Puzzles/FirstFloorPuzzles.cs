using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// A bank of numbered dials that must all be set to the right digit at once.
    ///
    /// Deliberately NOT a sequence — the corridor and the fuse box already test order. This tests
    /// deduction: nothing tells you the number, three separate documents each give you one piece of
    /// arithmetic, and the dials sit there until you have worked it out. There is no feedback per
    /// dial, only the lock releasing, so guessing a three-digit space by hand is real work while
    /// reading the notes makes it immediate.
    /// </summary>
    public class DialLockPuzzle : NetworkBehaviour
    {
        [SerializeField] private NetworkedDoorRef gatedDoor;
        [SerializeField] private string answer = "000";
        [SerializeField] private float knowledgeOnComplete = 6f;

        private readonly NetworkVariable<int> d0 = new NetworkVariable<int>();
        private readonly NetworkVariable<int> d1 = new NetworkVariable<int>();
        private readonly NetworkVariable<int> d2 = new NetworkVariable<int>();
        private readonly NetworkVariable<bool> solved = new NetworkVariable<bool>();

        public bool IsSolved => solved.Value;
        public int Digit(int i) => i == 0 ? d0.Value : i == 1 ? d1.Value : d2.Value;

        /// <summary>Fires on every peer whenever a dial moves, so the visible numerals can update.</summary>
        public event System.Action DialsChanged;

        public override void OnNetworkSpawn()
        {
            d0.OnValueChanged += (_, __) => DialsChanged?.Invoke();
            d1.OnValueChanged += (_, __) => DialsChanged?.Invoke();
            d2.OnValueChanged += (_, __) => DialsChanged?.Invoke();
            solved.OnValueChanged += (_, __) => DialsChanged?.Invoke();
            DialsChanged?.Invoke();
        }

        [ServerRpc(RequireOwnership = false)]
        public void TurnDialServerRpc(int index, ServerRpcParams p = default)
        {
            if (solved.Value) return;

            // Every turn is audible. Brute-forcing 1000 combinations by hand is possible in theory
            // and suicidal in practice, which is exactly the intended pressure.
            GameEvents.RaiseNoiseEmitted(transform.position, 7f, NoiseSource.PuzzleInteraction);

            if (index == 0) d0.Value = (d0.Value + 1) % 10;
            else if (index == 1) d1.Value = (d1.Value + 1) % 10;
            else d2.Value = (d2.Value + 1) % 10;

            // The run's combination, not the one baked in at build time.
            if ($"{d0.Value}{d1.Value}{d2.Value}" != RunCodes.Dials) return;

            solved.Value = true;
            ulong who = p.Receive.SenderClientId;
            gatedDoor?.Unlock();
            GameEvents.RaisePuzzleStepCompleted("ff_dials", who);
            LastWard.Knowledge.KnowledgeService.Instance?.AddScore(who, knowledgeOnComplete);
        }
    }

}
