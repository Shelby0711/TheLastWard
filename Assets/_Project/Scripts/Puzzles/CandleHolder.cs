using System.Collections;
using LastWard.Core;
using LastWard.Player;
using Unity.Netcode;
using UnityEngine;

namespace LastWard.Puzzles
{
    /// <summary>
    /// One candle on the asylum's route, and the three seconds it costs to light.
    ///
    /// The strike is deliberately a held action rather than a keypress. "Be quick about it" only bites
    /// if lighting takes time — tap-and-walk-away is no exposure at all. Three seconds kneeling with
    /// the flame shielded means deliberately standing in the one state the Manager punishes hardest,
    /// watching the meter climb, choosing when to break. Flinch and the match is gone; that costs a
    /// candle-minute later rather than a life now, which is the cheapest punishment available.
    ///
    /// Once lit it is a <see cref="WorldLight"/>, so standing in the pool is as damning as sweeping a
    /// torch — but carrying no penalty for merely being near it. That asymmetry is the whole point of
    /// candles over torches on this floor.
    ///
    /// Burns down. A permanent trail would make the floor progressively safer and drain the tension
    /// out of it; a decaying one means the path you lit on the way in may be dark by the time you are
    /// running back down it, and you will not know which stretch until you are already sprinting.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CandleHolder : NetworkBehaviour, IInteractable
    {
        [SerializeField] private Renderer flame;
        [SerializeField] private Light glow;
        [SerializeField] private WorldLight pool;

        [Tooltip("Seconds of kneeling. Any input during this wastes the match.")]
        [SerializeField] private float strikeSeconds = 3f;
        [Tooltip("Burn time. Ten matches at five minutes is fifty candle-minutes for the whole run.")]
        [SerializeField] private float burnSeconds = 300f;
        [SerializeField] private string requiredItemId = "matchbox";

        // Declared before anything that reads it in an OnValueChanged: NGO deserialises in order.
        private readonly NetworkVariable<bool> lit = new NetworkVariable<bool>();

        public bool IsLit => lit.Value;

        private Coroutine burn;
        private static bool localBusy;      // one strike at a time per client

        public override void OnNetworkSpawn()
        {
            lit.OnValueChanged += (_, __) => Apply();
            Apply();
        }

        private void Apply()
        {
            if (flame != null) flame.enabled = lit.Value;
            if (glow != null) glow.enabled = lit.Value;
            // Enabled state IS the registration - see WorldLight.
            if (pool != null) pool.enabled = lit.Value;
        }

        // ---- interaction ----

        public string GetPrompt()
        {
            if (lit.Value) return null;
            if (MatchSupply.Instance != null && !MatchSupply.Instance.Any) return "Candle — no matches left";
            var inv = PlayerInventory.Local;
            if (inv == null || !inv.HasItem(requiredItemId)) return "Candle — nothing to light it with";
            return "Hold to strike a match";
        }

        public bool CanInteract(ulong playerId)
        {
            if (lit.Value || localBusy) return false;
            if (MatchSupply.Instance != null && !MatchSupply.Instance.Any) return false;
            var inv = PlayerInventory.Local;
            return inv != null && inv.HasItem(requiredItemId);
        }

        public void Interact(ulong playerId)
        {
            if (!CanInteract(playerId)) return;
            var motor = PlayerInventory.Local != null
                ? PlayerInventory.Local.GetComponent<FirstPersonMotor>() : null;
            StartCoroutine(Strike(motor));
        }

        private IEnumerator Strike(FirstPersonMotor motor)
        {
            localBusy = true;
            // Kneels on its own. Making the player hold C as well would mean the "any input cancels"
            // rule fought the crouch they were required to hold, which is unplayable.
            if (motor != null) motor.ScriptedCrouch = true;

            float t = 0f;
            bool spoiled = false;
            // One frame of grace so the keypress that STARTED this doesn't immediately cancel it.
            yield return null;

            // The strike itself, for as long as it lasts. Played 2D on the striker only — this is a
            // sound happening at your own hands, not across the room.
            var strike = LastWard.Audio.GameSfx.MatchStrike;
            if (strike != null) LastWard.Audio.GameSfx.Play2D(strike, 0.8f);

            while (t < strikeSeconds)
            {
                if (AnyInput())
                {
                    spoiled = true;
                    break;
                }
                t += Time.deltaTime;
                // Without this the three seconds are indistinguishable from the game ignoring you,
                // and since any input burns the match, a player who doesn't know it started will
                // move, lose a match, and never find out why.
                LastWard.UI.StrikeMeterUI.Instance?.SetProgress(t / strikeSeconds);
                yield return null;
            }

            LastWard.UI.StrikeMeterUI.Instance?.Hide();
            if (motor != null) motor.ScriptedCrouch = false;
            localBusy = false;
            StrikeServerRpc(!spoiled);
        }

        private static bool AnyInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var ms = UnityEngine.InputSystem.Mouse.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            if (ms != null && (ms.leftButton.wasPressedThisFrame || ms.rightButton.wasPressedThisFrame))
                return true;
            var reader = PlayerInputReader.Local;
            return reader != null && reader.Move.sqrMagnitude > 0.02f;
        }

        // ---- server ----

        [ServerRpc(RequireOwnership = false)]
        private void StrikeServerRpc(bool succeeded)
        {
            if (lit.Value) return;
            var supply = MatchSupply.Instance;
            // The match is spent either way. A wasted one is the whole point of the flinch rule.
            if (supply != null && !supply.ServerSpend()) return;

            // The strike flare is a real noise and a real flash even when it fails - you have still
            // just made light and sound in the dark.
            GameEvents.RaiseNoiseEmitted(transform.position, 5f, NoiseSource.PuzzleInteraction);
            if (!succeeded) return;

            lit.Value = true;
            if (burn != null) StopCoroutine(burn);
            burn = StartCoroutine(BurnDown());
        }

        private IEnumerator BurnDown()
        {
            yield return new WaitForSeconds(burnSeconds);
            lit.Value = false;
            burn = null;
        }
    }
}
