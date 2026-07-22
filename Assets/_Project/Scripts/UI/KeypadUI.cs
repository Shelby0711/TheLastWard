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

        public void Open(RecordCodePuzzle puzzle)
        {
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
