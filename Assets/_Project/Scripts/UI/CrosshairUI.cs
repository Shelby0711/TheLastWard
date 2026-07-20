using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// Center aim dot. The interaction raycast already fires from screen center, so this just makes
    /// the aim point visible; it brightens/grows when an interactable is targeted (driven by
    /// PlayerInteractor) so the player gets feedback before the text prompt.
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        public static CrosshairUI Instance { get; private set; }

        [SerializeField] private Image dot;
        [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] private Color targetedColor = new Color(1f, 0.9f, 0.4f, 0.95f);
        [SerializeField] private float idleSize = 7f;
        [SerializeField] private float targetedSize = 11f;

        private void Awake()
        {
            Instance = this;
            SetTargeted(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetTargeted(bool targeted)
        {
            if (dot == null) return;
            dot.color = targeted ? targetedColor : idleColor;
            float size = targeted ? targetedSize : idleSize;
            dot.rectTransform.sizeDelta = new Vector2(size, size);
        }
    }
}
