using LastWard.Audio;
using LastWard.Core;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// Shows on a LIVING player's screen when a dead teammate pings them — a brief marker + chime
    /// signalling "one of the dead flagged something in your view." Since the spectator sees the
    /// exact same frame (no free look), this is an attention pulse, not a precise pointer; a
    /// world-space raycast ping is a possible later upgrade.
    /// </summary>
    public class SpectatorPingUI : MonoBehaviour
    {
        public static SpectatorPingUI Instance { get; private set; }

        [SerializeField] private GameObject marker;
        [SerializeField] private float visibleSeconds = 1.2f;

        private void Awake()
        {
            Instance = this;
            if (marker != null) marker.SetActive(false);
            GameEvents.OnSpectatorPing += Pulse;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameEvents.OnSpectatorPing -= Pulse;
        }

        public void Pulse()
        {
            if (marker != null)
            {
                marker.SetActive(true);
                CancelInvoke(nameof(HideMarker));
                Invoke(nameof(HideMarker), visibleSeconds);
            }
            var listener = FindFirstObjectByType<AudioListener>();
            AudioSource.PlayClipAtPoint(ProceduralSfx.PingChime(), listener != null ? listener.transform.position : Vector3.zero);
        }

        private void HideMarker()
        {
            if (marker != null) marker.SetActive(false);
        }
    }
}
