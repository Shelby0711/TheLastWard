using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>The bible's "choice beat" (§7): a persistent on-screen line telling a player, once
    /// they reach the exit area with teammates still alive, that the decision of who leaves is
    /// theirs to make — not just a silent race to a door.</summary>
    public class ExitWarningUI : MonoBehaviour
    {
        public static ExitWarningUI Instance { get; private set; }

        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text label;

        private void Awake()
        {
            Instance = this;
            if (root != null) root.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Show(string message)
        {
            if (label != null) label.text = message;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
