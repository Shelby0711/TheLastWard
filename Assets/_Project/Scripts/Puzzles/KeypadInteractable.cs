using LastWard.Core;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Puzzles
{
    public class KeypadInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private RecordCodePuzzle puzzle;
        /// <summary>The lit strip across the pad's face. Optional — older scenes have none.</summary>
        [SerializeField] private Renderer indicator;

        private static readonly Color Locked = new Color(0.75f, 0.06f, 0.04f);
        private static readonly Color Open = new Color(0.15f, 0.85f, 0.45f);

        private Material lamp;
        private bool lastSolved;

        private void Start()
        {
            // The pad shipped with a permanently green display, which reads as "already unlocked"
            // on a door that is very much locked. Red until the code is right.
            if (indicator == null) return;
            lamp = indicator.material;          // an instance: this pad's lamp, not every pad's
            Paint(false);
        }

        private void Update()
        {
            if (lamp == null || puzzle == null) return;
            bool solved = puzzle.IsSolved;
            if (solved == lastSolved) return;
            lastSolved = solved;
            Paint(solved);
        }

        private void Paint(bool solved)
        {
            var c = solved ? Open : Locked;
            lamp.SetColor("_BaseColor", c * 0.35f);
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", c);
        }

        public string GetPrompt() => puzzle != null && puzzle.IsSolved ? "Access granted" : "Enter code";
        public bool CanInteract(ulong playerId) => puzzle != null && !puzzle.IsSolved;
        public void Interact(ulong playerId) => KeypadUI.Instance?.Open(puzzle);
    }
}
