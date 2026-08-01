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
    /// <summary>The gate itself: needs the crowbar once the lock has released.</summary>
    public class GateBarInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private WardGatePuzzle puzzle;

        public string GetPrompt()
        {
            if (puzzle == null || puzzle.IsOpen) return null;
            if (!puzzle.IsPowered) return "Gate — the lock is dead";
            if (!puzzle.IsUnlocked) return "Gate — still locked";
            return PlayerInventory.Local != null && PlayerInventory.Local.HasItem("crowbar")
                ? "Lever the gate open"
                : "Gate — rusted solid (needs a crowbar)";
        }

        public bool CanInteract(ulong playerId) =>
            puzzle != null && puzzle.IsUnlocked && !puzzle.IsOpen &&
            PlayerInventory.Local != null && PlayerInventory.Local.HasItem("crowbar");

        public void Interact(ulong playerId)
        {
            PlayerInventory.Local?.RegisterUse("crowbar");
            puzzle.ForceOpenServerRpc();
        }
    }
}
