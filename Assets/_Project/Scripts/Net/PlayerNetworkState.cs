using System;
using LastWard.Core;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Net
{
    /// <summary>
    /// Per-player state other clients must see: camera pitch and flashlight on/off (so teammates —
    /// and dead spectators watching through their eyes — see the same view), plus alive/dead.
    /// Enabled on EVERY copy (not owner-only), unlike the input/camera components, so remote clients
    /// apply the flashlight and the spectator can read the watched player's view.
    /// </summary>
    public class PlayerNetworkState : NetworkBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Light flashlight;

        private readonly NetworkVariable<float> pitch =
            new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> flashlightOn =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> alive = new NetworkVariable<bool>(true); // server-write

        public Transform CameraPivot => cameraPivot;
        public float Pitch => pitch.Value;
        public bool IsAlive => alive.Value;
        public bool IsLocalPlayer => IsOwner;

        /// <summary>Fires locally on every client when alive changes; passes the new alive value.</summary>
        public event Action<bool> AliveChanged;

        public override void OnNetworkSpawn()
        {
            flashlightOn.OnValueChanged += OnFlashlightChanged;
            alive.OnValueChanged += OnAliveChanged;
            ApplyFlashlight(flashlightOn.Value);
        }

        public override void OnNetworkDespawn()
        {
            flashlightOn.OnValueChanged -= OnFlashlightChanged;
            alive.OnValueChanged -= OnAliveChanged;
        }

        private void LateUpdate()
        {
            if (cameraPivot == null) return;
            if (IsOwner)
            {
                if (!alive.Value) return;
                float p = cameraPivot.localEulerAngles.x;
                if (p > 180f) p -= 360f;
                if (!Mathf.Approximately(p, pitch.Value)) pitch.Value = p;
            }
            else
            {
                // Apply the synced pitch to the remote player's rig so their head + flashlight beam
                // actually tilt up/down for everyone watching (teammates and spectators alike).
                cameraPivot.localRotation = Quaternion.Euler(pitch.Value, 0f, 0f);
            }
        }

        public void ToggleFlashlight()
        {
            if (IsOwner) flashlightOn.Value = !flashlightOn.Value;
        }

        /// <summary>Server-only. The Entity calls this when it catches this player.</summary>
        public void ServerKill()
        {
            if (IsServer) alive.Value = false;
        }

        // --- spectator ping relay ---

        public void SendPing(ulong watchedClientId)
        {
            if (IsOwner) PingServerRpc(watchedClientId);
        }

        [ServerRpc]
        private void PingServerRpc(ulong watchedClientId) => PingClientRpc(watchedClientId);

        [ClientRpc]
        private void PingClientRpc(ulong watchedClientId)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == watchedClientId)
                GameEvents.RaiseSpectatorPing();
        }

        private void OnFlashlightChanged(bool _, bool now) => ApplyFlashlight(now);
        private void ApplyFlashlight(bool on) { if (flashlight != null) flashlight.enabled = on; }

        private void OnAliveChanged(bool _, bool now)
        {
            if (!now) GameEvents.RaisePlayerDied(OwnerClientId);
            AliveChanged?.Invoke(now);
        }
    }
}
