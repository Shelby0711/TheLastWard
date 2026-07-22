using LastWard.Net;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// Placed around the Exit Route's approach. Purely a local/client-side check — no networking
    /// needed since it's informational, not a state change: when the LOCAL player enters, count
    /// currently-alive players (readable by everyone, since PlayerNetworkState.alive has Everyone
    /// read permission) and show the choice-beat warning if more than one is still alive.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ExitChoiceZone : MonoBehaviour
    {
        [SerializeField] private string warningMessage = "Only one of you can leave.";

        private void OnTriggerEnter(Collider other)
        {
            var pns = other.GetComponentInParent<PlayerNetworkState>();
            if (pns == null || !pns.IsLocalPlayer) return;

            int aliveCount = 0;
            foreach (var p in FindObjectsByType<PlayerNetworkState>())
                if (p.IsAlive) aliveCount++;

            if (aliveCount > 1) ExitWarningUI.Instance?.Show(warningMessage);
        }

        private void OnTriggerExit(Collider other)
        {
            var pns = other.GetComponentInParent<PlayerNetworkState>();
            if (pns != null && pns.IsLocalPlayer) ExitWarningUI.Instance?.Hide();
        }
    }
}
