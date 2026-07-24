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
        [Tooltip("First-person flashlight model, shown only while the light is on. Owner-only — it " +
            "lives under the player camera, which is disabled on remote copies.")]
        [SerializeField] private GameObject flashlightModel;

        private readonly NetworkVariable<float> pitch =
            new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> flashlightOn =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        // Replicated because the Entity's senses run on the SERVER and need to know whether this
        // player is crouching. Owner-written, since only the owner runs the motor.
        private readonly NetworkVariable<bool> crouching =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> alive = new NetworkVariable<bool>(true); // server-write

        // How close the Entity is to having found this player, 0..1. The Entity (server) is the only
        // thing that writes it; everyone can read it so a spectator sees their host's meter too.
        private readonly NetworkVariable<float> discovery = new NetworkVariable<float>(0f);
        // Set while the player is inside a HidingSpot. Server-written so the Entity's own senses and
        // every client's view of it agree.
        private readonly NetworkVariable<bool> hidden = new NetworkVariable<bool>(false);
        // Set while the Entity has physically caught this player. Movement and look input are
        // suspended for its duration - the catch is a held beat, and a victim who can simply walk
        // out of it is not caught at all.
        private readonly NetworkVariable<bool> held = new NetworkVariable<bool>(false);

        public Transform CameraPivot => cameraPivot;
        public float Pitch => pitch.Value;
        public bool IsAlive => alive.Value;
        // Deliberately shadows NetworkBehaviour.IsLocalPlayer. The base version also requires the
        // NetworkObject to be flagged as a player object; ownership is the check every call site
        // here actually wants, so `new` keeps that behaviour rather than silently changing it.
        public new bool IsLocalPlayer => IsOwner;
        public bool FlashlightOn => flashlightOn.Value;
        public bool IsCrouching => crouching.Value;

        /// <summary>Pushed by the owner's motor each frame it changes.</summary>
        public void SetCrouching(bool value)
        {
            if (IsOwner && crouching.Value != value) crouching.Value = value;
        }
        public float Discovery => discovery.Value;
        public bool IsHidden => hidden.Value;
        public bool IsHeld => held.Value;

        /// <summary>Server-only. The Entity takes hold of this player for the catch sequence.</summary>
        public void ServerSetHeld(bool value)
        {
            if (IsServer) held.Value = value;
        }

        public void ServerSetDiscovery(float value)
        {
            if (IsServer) discovery.Value = Mathf.Clamp01(value);
        }

        public void ServerSetHidden(bool value)
        {
            if (IsServer) hidden.Value = value;
        }

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

        private void ApplyFlashlight(bool on)
        {
            if (flashlight != null) flashlight.enabled = on;
            // The held model appears and disappears with the beam, so the torch reads as something
            // the player is actually carrying rather than a light source floating in their face.
            if (flashlightModel != null) flashlightModel.SetActive(on);
        }

        private void OnAliveChanged(bool _, bool now)
        {
            if (!now) GameEvents.RaisePlayerDied(OwnerClientId);
            AliveChanged?.Invoke(now);
        }
    }
}
