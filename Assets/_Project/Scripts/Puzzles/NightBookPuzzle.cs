// Split out of its original combined file. Unity resolves a MonoBehaviour's MonoScript by FILENAME:
// a component class that does not match the file it lives in cannot be serialised into a scene, so
// AddComponent at build time produced a component that arrived broken and silently did nothing.
// Every MonoBehaviour therefore gets its own file.
using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// The night book on the Manager's desk, and the way off this floor.
    ///
    /// The Receptionist downstairs counts people in and out; the Manager decides who is written down
    /// as leaving. The stairs stay shut until a name is entered in the book — and the book takes
    /// <b>one name</b>. In co-op that is the moment the group has to say out loud who is being signed
    /// out, which is the whole "one player survives" idea arriving as a mechanic rather than an
    /// ending. Everyone can still climb the stairs; only the signed name is recorded as having left.
    /// </summary>
    public class NightBookPuzzle : NetworkBehaviour, IInteractable
    {
        [SerializeField] private NetworkedDoorRef stairsDoor;
        [SerializeField] private float knowledgeOnComplete = 8f;

        private readonly NetworkVariable<bool> signed = new NetworkVariable<bool>();
        private readonly NetworkVariable<ulong> signedBy = new NetworkVariable<ulong>();

        public bool IsSigned => signed.Value;
        /// <summary>Which client is written in the book. The ending will care about this.</summary>
        public ulong SignedBy => signedBy.Value;

        public string GetPrompt() => signed.Value
            ? "The night book — a name is already written"
            : "Sign the night book  [one name only]";

        public bool CanInteract(ulong playerId) => !signed.Value;
        public void Interact(ulong playerId) => SignServerRpc();

        [ServerRpc(RequireOwnership = false)]
        private void SignServerRpc(ServerRpcParams p = default)
        {
            if (signed.Value) return;
            ulong who = p.Receive.SenderClientId;

            signed.Value = true;
            signedBy.Value = who;
            stairsDoor?.Unlock();

            GameEvents.RaiseNoiseEmitted(transform.position, 14f, NoiseSource.PuzzleInteraction);
            GameEvents.RaisePuzzleStepCompleted("ff_nightbook", who);
            LastWard.Knowledge.KnowledgeService.Instance?.AddScore(who, knowledgeOnComplete);
        }
    }
}
