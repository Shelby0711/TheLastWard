using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>Dead-view overlay: a faint tint (the "watching through the hospital's memory" feel)
    /// plus a label naming whose eyes you're borrowing. True desaturation/static is an M8 polish;
    /// the tint is enough to read as "not your body anymore" in greybox.</summary>
    public class SpectatorUI : MonoBehaviour
    {
        public static SpectatorUI Instance { get; private set; }

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

        public void Show(ulong watchedClientId)
        {
            if (root != null) root.SetActive(true);
            if (label != null) label.text = $"Watching  ·  Player {watchedClientId}   [Q/E] switch   [LMB] ping";
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }
    }
}
