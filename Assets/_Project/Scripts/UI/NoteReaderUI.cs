using LastWard.Player;
using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    public class NoteReaderUI : MonoBehaviour
    {
        public static NoteReaderUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;

        private void Awake()
        {
            Instance = this;
            root.SetActive(false);
        }

        private PlayerInputReader subscribedInput;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Unsubscribe();
        }

        // Subscribe when the note actually opens rather than at Start: in co-op the local player
        // is spawned by Netcode after the scene loads, so PlayerInputReader.Local is still null
        // during Start and Escape would never bind.
        public void Show(string title, string body)
        {
            titleText.text = title;
            bodyText.text = body;
            root.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Unsubscribe();
            subscribedInput = PlayerInputReader.Local;
            if (subscribedInput != null) subscribedInput.PausePressed += Close;
        }

        public void Close()
        {
            Unsubscribe();
            root.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Unsubscribe()
        {
            if (subscribedInput != null) subscribedInput.PausePressed -= Close;
            subscribedInput = null;
        }
    }
}
