using LastWard.Core;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Puzzles
{
    public class KeypadInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private RecordCodePuzzle puzzle;

        public string GetPrompt() => puzzle != null && puzzle.IsSolved ? "Access granted" : "Enter code";
        public bool CanInteract(ulong playerId) => puzzle != null && !puzzle.IsSolved;
        public void Interact(ulong playerId) => KeypadUI.Instance?.Open(puzzle);
    }
}
