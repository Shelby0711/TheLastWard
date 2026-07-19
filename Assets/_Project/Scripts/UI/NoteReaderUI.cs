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

        private void Start()
        {
            // Subscribing here (not OnEnable) guarantees PlayerInputReader.Awake has already
            // run and set Local, since Unity runs all Awake calls before any Start call.
            if (PlayerInputReader.Local != null)
                PlayerInputReader.Local.PausePressed += Close;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (PlayerInputReader.Local != null)
                PlayerInputReader.Local.PausePressed -= Close;
        }

        public void Show(string title, string body)
        {
            titleText.text = title;
            bodyText.text = body;
            root.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            root.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
