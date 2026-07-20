using LastWard.Net;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Core
{
    /// <summary>
    /// One-shot zone-entry trigger: when any player crosses it, the server advances the shared
    /// objective stage. No NetworkObject needed here — the host's own physics simulation sees every
    /// player's synced collider locally, so the IsServer check alone is enough to keep this
    /// authoritative.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ObjectiveZoneTrigger : MonoBehaviour
    {
        [SerializeField] private ObjectiveStage stageOnEnter;

        private void OnTriggerEnter(Collider other)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (other.GetComponentInParent<NetworkPlayer>() == null) return;
            ObjectiveTracker.Instance?.ServerAdvanceTo(stageOnEnter);
        }
    }
}
