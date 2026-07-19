using LastWard.Core;
using LastWard.Puzzles;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Player
{
    public class NoteInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private ClueDefinition clue;

        public string GetPrompt() => clue != null ? $"Read {clue.displayTitle}" : "Read";
        public bool CanInteract(ulong playerId) => clue != null;

        public void Interact(ulong playerId)
        {
            if (clue == null) return;
            NoteReaderUI.Instance?.Show(clue.displayTitle, clue.bodyText);
        }
    }
}
