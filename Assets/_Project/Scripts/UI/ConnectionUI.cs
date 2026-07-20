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
            if (NetworkSessionManager.Instance != null)
                NetworkSessionManager.Instance.StatusChanged += OnStatus;
        }

        private void OnDestroy()
        {
            if (NetworkSessionManager.Instance != null)
                NetworkSessionManager.Instance.StatusChanged -= OnStatus;
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
