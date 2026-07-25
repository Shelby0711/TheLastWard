using LastWard.Net;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// Past the corridor, with the door shut behind you, the pressure lets go.
    ///
    /// The Receptionist's territory ends at that door — it is the thing that has been counting you,
    /// and once you are through and it is closed you are no longer on its floor. Mechanically this
    /// clears the fear meter, which matters for pacing as much as fiction: the corridor is a long
    /// stretch of accumulating dread, and without a defined release the player arrives upstairs with
    /// a nearly-full meter and dies to the Manager for something the Receptionist did.
    ///
    /// The door genuinely has to be SHUT. Sprinting through and leaving it swinging keeps the corridor
    /// connected to the stairwell, so the reprieve has to be earned by turning round and closing it —
    /// a deliberately vulnerable few seconds with your back to everything you just escaped.
    /// </summary>
    public class SanctuaryZone : MonoBehaviour
    {
        [Tooltip("Players at or beyond this Z have left the corridor.")]
        [SerializeField] private float safeFromZ = 54.6f;
        [Tooltip("...and BELOW this Z are still in the stairwell. The sanctuary is the shaft between " +
            "the exit door and the first floor - nothing beyond it. Without this bound it covered " +
            "the whole upper building and quietly drained the Manager's meter faster than the " +
            "Manager could fill it, so the first floor had no threat at all.")]
        [SerializeField] private float safeToZ = 60.5f;
        [Tooltip("And below this height: once you have climbed to the first floor you are on the " +
            "Manager's ground and the reprieve is over.")]
        [SerializeField] private float safeBelowY = 3.0f;
        [Tooltip("The door that has to be closed behind them.")]
        [SerializeField] private NetworkedDoor gateDoor;
        [Tooltip("How fast the meter bleeds away once safe. Not instant — it should feel like the " +
            "breath going out of you, not a switch.")]
        [SerializeField] private float relaxPerSecond = 0.6f;

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            // Still open? Then the corridor is still connected and nothing has been escaped yet.
            if (gateDoor != null && gateDoor.IsOpen) return;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var po = client.PlayerObject;
                if (po == null) continue;
                Vector3 at = po.transform.position;
                if (at.z < safeFromZ || at.z > safeToZ || at.y > safeBelowY) continue;
                if (!po.TryGetComponent<PlayerNetworkState>(out var pns) || !pns.IsAlive) continue;
                if (pns.Discovery <= 0f) continue;

                pns.ServerSetDiscovery(Mathf.Max(0f, pns.Discovery - relaxPerSecond * Time.deltaTime));
            }
        }
    }
}
