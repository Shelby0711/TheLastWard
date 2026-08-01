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
    /// <summary>The pad beside the gate. Dark until the generator runs.</summary>
    public class GateKeypadInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private WardGatePuzzle puzzle;

        public string GetPrompt()
        {
            if (puzzle == null) return null;
            if (!puzzle.IsPowered) return "Keypad — no power";
            return puzzle.IsUnlocked ? "Keypad — accepted" : "Enter code";
        }

        public bool CanInteract(ulong playerId) =>
            puzzle != null && puzzle.IsPowered && !puzzle.IsUnlocked;

        public void Interact(ulong playerId) =>
            LastWard.UI.KeypadUI.Instance?.Open(entered => puzzle.SubmitCodeServerRpc(entered));
    }
}
