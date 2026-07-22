using LastWard.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace LastWard.UI
{
    /// <summary>
    /// Shows how close the Entity is to having found you. Hidden entirely at zero — a meter that is
    /// always on screen reads as a HUD element, whereas one that only appears when something is
    /// looking at you IS the scare.
    ///
    /// Reads the local player's replicated value rather than tracking anything itself, so what the
    /// bar shows always matches what the Entity's senses actually decided on the server.
    /// </summary>
    public class DiscoveryMeterUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image fill;

        [SerializeField] private Color calmColor = new Color(0.85f, 0.78f, 0.35f);
        [SerializeField] private Color foundColor = new Color(0.8f, 0.12f, 0.1f);

        // Below this the meter stays hidden, so a single stray glance across a corridor doesn't
        // flash the bar on and off.
        private const float VisibilityThreshold = 0.04f;

        private PlayerNetworkState local;

        private void Update()
        {
            if (group == null || fill == null) return;

            if (local == null || !local.IsSpawned) local = FindLocalPlayer();
            float discovery = local != null && local.IsAlive ? local.Discovery : 0f;

            float targetAlpha = discovery > VisibilityThreshold ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, Time.deltaTime * 4f);

            fill.fillAmount = discovery;
            fill.color = Color.Lerp(calmColor, foundColor, discovery);

            // A pulse once it's nearly full — the last warning before it commits.
            if (discovery > 0.75f)
            {
                float pulse = 0.75f + Mathf.PingPong(Time.time * 3.5f, 0.25f);
                fill.color *= pulse;
            }
        }

        private static PlayerNetworkState FindLocalPlayer()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.LocalClient == null) return null;
            var playerObject = nm.LocalClient.PlayerObject;
            return playerObject != null ? playerObject.GetComponent<PlayerNetworkState>() : null;
        }
    }
}
