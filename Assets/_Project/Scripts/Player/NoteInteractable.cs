using LastWard.Core;
using LastWard.Knowledge;
using LastWard.Puzzles;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Player
{
    public class NoteInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private ClueDefinition clue;

        // Per-client: prevents the local player from farming the same note's knowledge. Different
        // players run on different clients, so each still gets credited once for reading it.
        private bool readLocally;

        public string GetPrompt() => clue != null ? $"Read {clue.displayTitle}" : "Read";
        public bool CanInteract(ulong playerId) => clue != null;

        public void Interact(ulong playerId)
        {
            if (clue == null) return;
            NoteReaderUI.Instance?.Show(clue.displayTitle, clue.bodyText);

            if (!readLocally)
            {
                readLocally = true;
                KnowledgeService.Instance?.ReportLocalKnowledge(clue.knowledgeValue);
            }
        }
    }
}
