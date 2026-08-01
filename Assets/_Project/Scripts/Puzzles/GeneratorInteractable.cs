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
    /// <summary>The generator by the stairs: takes the heavy cell, then the lever.</summary>
    public class GeneratorInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private WardGatePuzzle puzzle;

        public string GetPrompt()
        {
            if (puzzle == null) return null;
            if (puzzle.IsPowered) return "Generator — running";
            return PlayerInventory.Local != null && PlayerInventory.Local.HasItem("cell")
                ? "Fit the cell and throw the lever"
                : "Generator — dead (needs a heavy cell)";
        }

        public bool CanInteract(ulong playerId) =>
            puzzle != null && !puzzle.IsPowered &&
            PlayerInventory.Local != null && PlayerInventory.Local.HasItem("cell");

        public void Interact(ulong playerId)
        {
            PlayerInventory.Local.RemoveItem("cell");
            puzzle.PowerOnServerRpc();
        }
    }
}
