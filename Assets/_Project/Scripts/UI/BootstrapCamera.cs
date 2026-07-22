using Unity.Netcode;
using UnityEngine;

namespace LastWard.UI
{
    /// <summary>
    /// Renders the connection menu before any player exists (the player — and its camera — only
    /// spawns after Host/Join). Once the local player object is spawned, this hands off: it
    /// disables itself so the owner's player camera takes over, and re-enables if that player goes.
    /// The AudioListener beside it is toggled in lockstep: without one here Unity logs "There are
    /// no audio listeners in the scene" every frame while you sit in the menu, but leaving it on
    /// after the player spawns would collide with the player camera's own listener.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class BootstrapCamera : MonoBehaviour
    {
        private Camera cam;
        private AudioListener listener;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            listener = GetComponent<AudioListener>();
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            bool hasLocalPlayer = nm != null && nm.IsListening &&
                                  nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
            if (cam.enabled == hasLocalPlayer) cam.enabled = !hasLocalPlayer;
            if (listener != null && listener.enabled == hasLocalPlayer) listener.enabled = !hasLocalPlayer;
        }
    }
}
