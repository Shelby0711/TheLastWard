using System.Collections.Generic;
using LastWard.Net;
using LastWard.Player;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Spectator
{
    /// <summary>
    /// Owner-only tethered spectator (bible §6): a dead player slaves their own camera to a living
    /// teammate's synced view — position from the watched CameraPivot, yaw from the watched root,
    /// pitch from PlayerNetworkState. No free look, so they see exactly what that player sees, which
    /// is what naturally prevents omniscience (note text and off-frustum threats never reach them).
    /// Q/E cycle targets (cooldown); Ping flags attention on the watched player's own screen.
    /// </summary>
    public class SpectatorController : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerNetworkState state;
        [SerializeField] private Camera spectatorCamera;
        [SerializeField] private float switchCooldown = 5f;
        [SerializeField] private float pingCooldown = 30f;

        private bool active;
        private PlayerNetworkState watched;
        private float lastSwitch = -999f;
        private float lastPing = -999f;

        public void Activate()
        {
            if (active) return;
            active = true;
            input.SetSpectatorMode(true);
            input.SpectatorNextPressed += Next;
            input.SpectatorPreviousPressed += Previous;
            input.SpectatorPingPressed += Ping;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SelectFirstLiving();
        }

        private void OnDisable()
        {
            if (!active) return;
            input.SpectatorNextPressed -= Next;
            input.SpectatorPreviousPressed -= Previous;
            input.SpectatorPingPressed -= Ping;
        }

        private void LateUpdate()
        {
            if (!active) return;
            if (watched == null || !watched.IsAlive) SelectFirstLiving();
            if (watched == null) { SpectatorUI.Instance?.Hide(); return; }

            var pivot = watched.CameraPivot;
            if (pivot == null) return;
            spectatorCamera.transform.position = pivot.position;
            spectatorCamera.transform.rotation = watched.transform.rotation * Quaternion.Euler(watched.Pitch, 0f, 0f);
        }

        private void Next() => Cycle(1);
        private void Previous() => Cycle(-1);

        private void Cycle(int dir)
        {
            if (Time.time - lastSwitch < switchCooldown) return;
            var living = LivingPlayers();
            if (living.Count == 0) { watched = null; return; }
            int idx = living.IndexOf(watched);
            idx = ((idx + dir) % living.Count + living.Count) % living.Count;
            watched = living[idx];
            lastSwitch = Time.time;
            SpectatorUI.Instance?.Show(watched.OwnerClientId);
        }

        private void Ping()
        {
            if (watched == null || Time.time - lastPing < pingCooldown) return;
            lastPing = Time.time;
            state.SendPing(watched.OwnerClientId);
        }

        private void SelectFirstLiving()
        {
            var living = LivingPlayers();
            watched = living.Count > 0 ? living[0] : null;
            if (watched != null) SpectatorUI.Instance?.Show(watched.OwnerClientId);
        }

        private List<PlayerNetworkState> LivingPlayers()
        {
            var result = new List<PlayerNetworkState>();
            foreach (var s in FindObjectsByType<PlayerNetworkState>(FindObjectsSortMode.InstanceID))
                if (s != state && s.IsAlive) result.Add(s);
            return result;
        }

        public static bool AnyOtherAlive(PlayerNetworkState self)
        {
            foreach (var s in FindObjectsByType<PlayerNetworkState>(FindObjectsSortMode.InstanceID))
                if (s != self && s.IsAlive) return true;
            return false;
        }
    }
}
