using LastWard.Player;
using LastWard.Puzzles;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>Numeric-code entry panel opened by a KeypadInteractable. Auto-closes shortly after
    /// the target puzzle reports solved; wrong codes just leave the panel open to retry.</summary>
    public class KeypadUI : MonoBehaviour
    {
        public static KeypadUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private Button submitButton;

        private RecordCodePuzzle target;
        private PlayerInputReader subscribedInput;
        private bool closeScheduled;

        private void Awake()
        {
            Instance = this;
            submitButton.onClick.AddListener(OnSubmit);
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
            root.SetActive(true);
            CursorLockGate.PanelOpened();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

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
        }

        private void Update()
        {
            if (target != null && target.IsSolved && !closeScheduled)
            {
                closeScheduled = true;
                Invoke(nameof(Close), 0.4f);
            }
        }
    }
}
