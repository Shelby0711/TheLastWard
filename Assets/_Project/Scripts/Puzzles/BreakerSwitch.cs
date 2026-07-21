using System.Collections;
using LastWard.Core;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>Thin IInteractable wrapper: forwards the press to the puzzle controller with this
    /// switch's index; the controller checks whether it's the expected next step. Also reacts to
    /// the puzzle's replicated state to color itself green (correctly progressed) or flash red
    /// (just pressed wrong) so players can see which switches are right without guessing.</summary>
    public class BreakerSwitch : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private FusePowerPuzzle puzzle;
        [SerializeField] private int breakerIndex;
        [SerializeField] private string label = "breaker";
        [SerializeField] private float wrongFlashSeconds = 0.4f;

        private static readonly Color DefaultBase = new Color(0.3f, 0.25f, 0.05f);
        private static readonly Color DefaultEmission = new Color(0.6f, 0.5f, 0.1f);
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
                puzzle.WrongBreakerPressed += OnWrongBreakerPressed;
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
            puzzle.WrongBreakerPressed -= OnWrongBreakerPressed;
        }

        public string GetPrompt() => puzzle != null && puzzle.IsPowered ? $"Flip {label}" : "No power";
        public bool CanInteract(ulong playerId) => puzzle != null && puzzle.IsPowered && !puzzle.IsSolved;
        public void Interact(ulong playerId) => puzzle.RequestFlipBreakerServerRpc(breakerIndex);

        private void OnCorrectMaskChanged(int mask) => ApplyCorrectMask(mask);

        private void ApplyCorrectMask(int mask)
        {
            bool correct = (mask & (1 << breakerIndex)) != 0;
            SetColor(correct ? CorrectBase : DefaultBase, correct ? CorrectEmission : DefaultEmission);
        }

        private void OnWrongBreakerPressed(int wrongIndex)
        {
            if (wrongIndex != breakerIndex) return;
            StopAllCoroutines();
            StartCoroutine(FlashWrong());
        }

        private IEnumerator FlashWrong()
        {
            SetColor(WrongBase, WrongEmission);
            yield return new WaitForSeconds(wrongFlashSeconds);
            ApplyCorrectMask(puzzle.CorrectMask); // resets to 0 server-side on a wrong press, so this reverts to default
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
