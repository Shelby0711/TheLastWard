using LastWard.Core;
using LastWard.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWard.Player
{
    /// <summary>
    /// Emotes: the foundation, not the content.
    ///
    /// This is deliberately a thin, general pipe — press a number, the server validates and rebroadcasts,
    /// every client plays clip N on that player's body. Adding an emote later should mean adding a clip
    /// to <see cref="EmoteCatalog"/> and a row in the animator, not touching any of this.
    ///
    /// Two rules that exist for the horror rather than for tidiness:
    ///
    /// <list type="bullet">
    /// <item><b>Emotes make noise.</b> Not much, but some. In a game where the ground-floor entity hunts
    /// by sound, a free silent action would be the one thing players could always do safely, and "always
    /// safe" is corrosive here. Mucking about has to cost something.</item>
    /// <item><b>You cannot emote while held, hidden, or dead.</b> Nothing undercuts a catch sequence like
    /// being able to wave during it.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkState))]
    public class EmoteController : NetworkBehaviour
    {
        [SerializeField] private PlayerNetworkState state;
        [Tooltip("Noise an emote makes. Small, but not nothing — see the class comment.")]
        [SerializeField] private float emoteNoiseRadius = 5f;
        [Tooltip("Seconds before another can be played, so it cannot be spammed into a strobe.")]
        [SerializeField] private float cooldown = 1.5f;

        private float nextAllowed;

        private void Awake()
        {
            if (state == null) state = GetComponent<PlayerNetworkState>();
        }

        private void Update()
        {
            if (state == null || !state.IsLocalPlayer) return;
            if (Keyboard.current == null || Time.time < nextAllowed) return;

            // Nothing playful while something has hold of you.
            if (!state.IsAlive || state.IsHeld || state.IsHidden) return;

            int pressed = -1;
            if (Keyboard.current.f1Key.wasPressedThisFrame) pressed = 0;
            else if (Keyboard.current.f2Key.wasPressedThisFrame) pressed = 1;
            else if (Keyboard.current.f4Key.wasPressedThisFrame) pressed = 2;

            if (pressed < 0 || pressed >= EmoteCatalog.Count) return;
            nextAllowed = Time.time + cooldown;
            RequestEmoteServerRpc(pressed);
        }

        [ServerRpc]
        private void RequestEmoteServerRpc(int index)
        {
            if (index < 0 || index >= EmoteCatalog.Count) return;
            if (state != null && (!state.IsAlive || state.IsHeld || state.IsHidden)) return;

            GameEvents.RaiseNoiseEmitted(transform.position, emoteNoiseRadius, NoiseSource.Voice);
            PlayEmoteClientRpc(index);
        }

        [ClientRpc]
        private void PlayEmoteClientRpc(int index)
        {
            // Every copy plays it, including the owner's — the owner's own body is hidden in first
            // person, but a mirror or a spectating teammate should still see it.
            foreach (var driver in GetComponentsInChildren<PlayerAnimationDriver>(true))
                driver.PlayEmote(index);
        }
    }
}
