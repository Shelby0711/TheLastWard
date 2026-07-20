using Unity.Netcode;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// Renders the connection menu before any player exists (the player — and its camera — only
    /// spawns after Host/Join). Once the local player object is spawned, this hands off: it
    /// disables itself so the owner's player camera takes over, and re-enables if that player goes.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class BootstrapCamera : MonoBehaviour
    {
        private Camera cam;

        private void Awake() => cam = GetComponent<Camera>();

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool hasLocalPlayer = nm != null && nm.IsListening &&
                                  nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
            if (cam.enabled == hasLocalPlayer) cam.enabled = !hasLocalPlayer;
        }
    }
}
