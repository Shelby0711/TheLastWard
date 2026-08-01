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
    /// <summary>One dial. Interacting steps it 0-9.</summary>
    public class DialInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private DialLockPuzzle puzzle;
        [SerializeField] private int index;

        public string GetPrompt() =>
            puzzle == null ? null
            : puzzle.IsSolved ? "Released"
            : $"Turn dial  [{puzzle.Digit(index)}]";

        public bool CanInteract(ulong playerId) => puzzle != null && !puzzle.IsSolved;
        public void Interact(ulong playerId) => puzzle.TurnDialServerRpc(index);
    }
}
