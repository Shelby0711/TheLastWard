using LastWard.Audio;
using TMPro;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// Minimal ending overlay for the one-slot exit. Every client gets this call (broadcast
    /// ClientRpc), each showing the outcome from their own perspective — matches the bible's shared
    /// "farewell" beat rather than only the escapee seeing anything.
    /// </summary>
    public class EndingUI : MonoBehaviour
    {
        public static EndingUI Instance { get; private set; }

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

        public void Show(bool escaped)
        {
            if (label != null) label.text = escaped ? "YOU ESCAPED." : "THE HOSPITAL KEEPS YOU.";
            if (root != null) root.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Play at the local listener's position, not world origin — PlayClipAtPoint's temp
            // source is fully 3D by default, and origin could be far from wherever the player is.
            var listener = FindFirstObjectByType<AudioListener>();
            AudioSource.PlayClipAtPoint(ProceduralSfx.FarewellSting(), listener != null ? listener.transform.position : Vector3.zero);
        }
    }
}
