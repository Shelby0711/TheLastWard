using Unity.Netcode;
using UnityEngine;

namespace LastWard.Net
{
    /// <summary>
    /// Gates local-only components so only the owning client drives its own player. These
    /// components start DISABLED in the player prefab (so their Awake is harmless but OnEnable
    /// never runs on remote copies), and this enables them only for the owner on spawn. Remote
    /// players are moved purely by ClientNetworkTransform.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [Tooltip("Camera, AudioListener, input, motor, look, interactor, inventory, flashlight controller.")]
        [SerializeField] private Behaviour[] ownerOnlyBehaviours;

        public override void OnNetworkSpawn()
        {
            foreach (var behaviour in ownerOnlyBehaviours)
                if (behaviour != null) behaviour.enabled = IsOwner;
        }
    }
}
