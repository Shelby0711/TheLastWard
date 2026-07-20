using LastWard.Core;
using UnityEngine;

namespace LastWard.Puzzles
{
    public class IntercomStation : MonoBehaviour, IInteractable
    {
        [SerializeField] private IntercomPuzzle puzzle;
        [SerializeField] private int stationIndex;
        [SerializeField] private string label = "station";

        public string GetPrompt() => puzzle != null && puzzle.IsSolved ? $"{label} (active)" : $"Activate {label}";
        public bool CanInteract(ulong playerId) => puzzle != null && !puzzle.IsSolved;
        public void Interact(ulong playerId) => puzzle.RequestActivateServerRpc(stationIndex);
    }
}
