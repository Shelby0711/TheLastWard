using LastWard.Audio;
using LastWard.Player;
using LastWard.Puzzles;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// Numeric-code entry, opened by a KeypadInteractable. Auto-closes shortly after the target
    /// puzzle reports solved.
    ///
    /// A wrong code used to do nothing visible at all — the panel just sat there, so there was no
    /// way to tell a rejected code from a dropped keypress. Now the server answers every failed
    /// attempt on the submitting client alone (<see cref="ShowWrong"/>): the field flashes red and
    /// clears, and the same bang the other locks use plays. Everyone else in the building hears
    /// only the noise the puzzle already emits, which is the intended deterrent against guessing.
    /// </summary>
    public class KeypadUI : MonoBehaviour
    {
        public static KeypadUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private Button submitButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Image fieldImage;

        private static readonly Color FieldIdle = new Color(0.05f, 0.05f, 0.06f, 0.95f);
        private static readonly Color FieldWrong = new Color(0.30f, 0.05f, 0.04f, 0.95f);

        private RecordCodePuzzle target;
        private PlayerInputReader subscribedInput;
        private bool closeScheduled;
        private float wrongUntil;

        private void Awake()
        {
            Instance = this;
            submitButton.onClick.AddListener(OnSubmit);
            // Enter submits. Typing a code and then having to find the button with the mouse is
            // the kind of friction that reads as the panel being broken.
            if (codeInput != null) codeInput.onSubmit.AddListener(_ => OnSubmit());
            root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Unsubscribe();
        }

        // Generic entry point: any puzzle can hand the pad a submit handler. Kept alongside the
        // typed overload so the existing Ward keypad is untouched.
        private System.Action<string> submitHandler;

        public void Open(System.Action<string> onSubmit)
        {
            submitHandler = onSubmit;
            Open((RecordCodePuzzle)null);
        }

        public void Open(RecordCodePuzzle puzzle)
        {
            if (puzzle != null) submitHandler = null;
            target = puzzle;
            closeScheduled = false;
            codeInput.text = string.Empty;
            SetStatus(null);
            root.SetActive(true);
            CursorLockGate.PanelOpened();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Focus the field so the first digit typed actually lands in it.
            codeInput.Select();
            codeInput.ActivateInputField();

            Unsubscribe();
            subscribedInput = PlayerInputReader.Local;
            if (subscribedInput != null) subscribedInput.PausePressed += Close;
        }

        public void Close()
        {
            Unsubscribe();
            target = null;
            root.SetActive(false);
            CursorLockGate.PanelClosed();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Called on the submitting client only, by the puzzle, when the code was wrong.</summary>
        public void ShowWrong()
        {
            SetStatus("INCORRECT");
            codeInput.text = string.Empty;
            codeInput.ActivateInputField();
            wrongUntil = Time.time + 0.9f;
            if (fieldImage != null) fieldImage.color = FieldWrong;
            GameSfx.Play2D(GameSfx.Random(GameSfx.WrongAttempt), 0.7f);
        }

        private void SetStatus(string s)
        {
            if (statusText == null) return;
            statusText.text = s ?? string.Empty;
            statusText.color = MenuTheme.Accent;
        }

        private void Unsubscribe()
        {
            if (subscribedInput != null) subscribedInput.PausePressed -= Close;
            subscribedInput = null;
        }

        private void OnSubmit()
        {
            if (submitHandler != null)
            {
                submitHandler(codeInput.text);
                Close();
                return;
            }
            if (target == null) return;
            target.RequestSubmitCodeServerRpc(codeInput.text);
            codeInput.text = string.Empty;
            codeInput.ActivateInputField();
        }

        private void Update()
        {
            if (wrongUntil > 0f && Time.time >= wrongUntil)
            {
                wrongUntil = 0f;
                if (fieldImage != null) fieldImage.color = FieldIdle;
                SetStatus(null);
            }

            if (target != null && target.IsSolved && !closeScheduled)
            {
                closeScheduled = true;
                SetStatus(null);
                Invoke(nameof(Close), 0.4f);
            }
        }
    }
}
