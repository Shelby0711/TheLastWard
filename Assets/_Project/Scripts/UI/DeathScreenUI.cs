using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// Minimal death overlay for M3 so the Entity's kill is visible. Full death → tethered
    /// spectator handoff is M6; for now this just blacks the screen with a word.
    /// </summary>
    public class DeathScreenUI : MonoBehaviour
    {
        public static DeathScreenUI Instance { get; private set; }

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

        public void Show(string text)
        {
            if (label != null) label.text = text;
            if (root != null) root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
