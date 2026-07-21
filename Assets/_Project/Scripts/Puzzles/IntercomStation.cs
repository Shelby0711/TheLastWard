using System.Collections;
using LastWard.Core;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>Thin IInteractable wrapper: forwards the press to the puzzle controller with this
    /// station's index; reacts to the puzzle's replicated state to color itself green (correctly
    /// progressed) or flash red (just pressed wrong) — same feedback pattern as BreakerSwitch.</summary>
    public class IntercomStation : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private IntercomPuzzle puzzle;
        [SerializeField] private int stationIndex;
        [SerializeField] private string label = "station";
        [SerializeField] private float wrongFlashSeconds = 0.4f;

        private static readonly Color DefaultBase = new Color(0.15f, 0.15f, 0.05f);
        private static readonly Color DefaultEmission = new Color(0.5f, 0.45f, 0.1f);
        private static readonly Color CorrectBase = new Color(0.05f, 0.3f, 0.05f);
        private static readonly Color CorrectEmission = new Color(0.1f, 0.9f, 0.15f);
        private static readonly Color WrongBase = new Color(0.3f, 0.05f, 0.05f);
        private static readonly Color WrongEmission = new Color(0.9f, 0.1f, 0.1f);

        private Renderer rend;
        private MaterialPropertyBlock mpb;

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (puzzle != null)
            {
                puzzle.CorrectMaskChanged += OnCorrectMaskChanged;
                puzzle.WrongStationPressed += OnWrongStationPressed;
                ApplyCorrectMask(puzzle.CorrectMask);
            }
            else
            {
                SetColor(DefaultBase, DefaultEmission);
            }
        }

        private void OnDisable()
        {
            if (puzzle == null) return;
            puzzle.CorrectMaskChanged -= OnCorrectMaskChanged;
            puzzle.WrongStationPressed -= OnWrongStationPressed;
        }

        public string GetPrompt() => puzzle != null && puzzle.IsSolved ? $"{label} (active)" : $"Activate {label}";
        public bool CanInteract(ulong playerId) => puzzle != null && !puzzle.IsSolved;
        public void Interact(ulong playerId) => puzzle.RequestActivateServerRpc(stationIndex);

        private void OnCorrectMaskChanged(int mask) => ApplyCorrectMask(mask);

        private void ApplyCorrectMask(int mask)
        {
            bool correct = (mask & (1 << stationIndex)) != 0;
            SetColor(correct ? CorrectBase : DefaultBase, correct ? CorrectEmission : DefaultEmission);
        }

        private void OnWrongStationPressed(int wrongIndex)
        {
            if (wrongIndex != stationIndex) return;
            StopAllCoroutines();
            StartCoroutine(FlashWrong());
        }

        private IEnumerator FlashWrong()
        {
            SetColor(WrongBase, WrongEmission);
            yield return new WaitForSeconds(wrongFlashSeconds);
            ApplyCorrectMask(puzzle.CorrectMask);
        }

        private void SetColor(Color baseColor, Color emission)
        {
            rend.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, baseColor);
            mpb.SetColor(EmissionColorId, emission);
            rend.SetPropertyBlock(mpb);
        }
    }
}
