using LastWard.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    public class ConnectionUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_InputField codeInput;
        [SerializeField] private TMP_Text statusText;

        private void Awake()
        {
            hostButton.onClick.AddListener(OnHost);
            joinButton.onClick.AddListener(OnJoin);
        }

        private void Start()
        {
            if (NetworkSessionManager.Instance == null) return;
            NetworkSessionManager.Instance.StatusChanged += OnStatus;
            NetworkSessionManager.Instance.Disconnected += OnDisconnected;
        }

        private void OnDestroy()
        {
            if (NetworkSessionManager.Instance == null) return;
            NetworkSessionManager.Instance.StatusChanged -= OnStatus;
            NetworkSessionManager.Instance.Disconnected -= OnDisconnected;
        }

        // Bring the panel back so a dropped session is visible and re-hostable, rather than
        // leaving the player in a frozen world with no player object.
        private void OnDisconnected()
        {
            if (panel != null) panel.SetActive(true);
        }

        private void OnHost()
        {
            NetworkSessionManager.Instance.Host();
            if (panel != null) panel.SetActive(false);
        }

        private void OnJoin()
        {
            NetworkSessionManager.Instance.Join(codeInput.text);
            if (panel != null) panel.SetActive(false);
        }

        private void OnStatus(string status)
        {
            if (statusText != null) statusText.text = status;
        }
    }
}
