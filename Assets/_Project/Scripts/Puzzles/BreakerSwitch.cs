using LastWard.Core;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>Thin IInteractable wrapper: forwards the press to the puzzle controller with this
    /// switch's index; the controller checks whether it's the expected next step.</summary>
    public class BreakerSwitch : MonoBehaviour, IInteractable
    {
        [SerializeField] private FusePowerPuzzle puzzle;
        [SerializeField] private int breakerIndex;
        [SerializeField] private string label = "breaker";

        public string GetPrompt() => puzzle != null && puzzle.IsPowered ? $"Flip {label}" : "No power";
        public bool CanInteract(ulong playerId) => puzzle != null && puzzle.IsPowered && !puzzle.IsSolved;
        public void Interact(ulong playerId) => puzzle.RequestFlipBreakerServerRpc(breakerIndex);
    }
}
