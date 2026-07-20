using System.Collections;
using LastWard.Audio;
using LastWard.Net;
using LastWard.Spectator;
using LastWard.UI;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// Owner-side reaction to this player's death (PlayerNetworkState.alive -> false, set by the
    /// Entity on the server). Death is permanent now — the player freezes, sees a brief message,
    /// then becomes a tethered spectator if any teammate is still alive; otherwise the message
    /// stays (no one left to watch). The real M6 flow replaces the old placeholder respawn.
    /// </summary>
    public class PlayerDeath : MonoBehaviour
    {
        [SerializeField] private Behaviour[] disableOnDeath;
        [SerializeField] private PlayerNetworkState state;
        [SerializeField] private SpectatorController spectator;
        [SerializeField] private float messageSeconds = 2.5f;

        private void OnEnable() => state.AliveChanged += OnAliveChanged;
        private void OnDisable() => state.AliveChanged -= OnAliveChanged;

        private void OnAliveChanged(bool aliveNow)
        {
            if (aliveNow || !state.IsLocalPlayer) return;
            StartCoroutine(DeathRoutine());
        }

        private IEnumerator DeathRoutine()
        {
            foreach (var b in disableOnDeath)
                if (b != null) b.enabled = false;

            AudioSource.PlayClipAtPoint(ProceduralSfx.DeathSting(), transform.position);

            bool othersAlive = SpectatorController.AnyOtherAlive(state);
            DeathScreenUI.Instance?.Show(othersAlive ? "Now see them Die." : "No one will Miss You.");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            yield return new WaitForSeconds(messageSeconds);

            if (othersAlive)
            {
                DeathScreenUI.Instance?.Hide();
                spectator.Activate();
            }
            // else: no teammates left — leave the death screen up.
        }
    }
}
