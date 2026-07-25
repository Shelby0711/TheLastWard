using LastWard.Audio;
using LastWard.Net;
using UnityEngine;

namespace LastWard.Player
{
    /// <summary>
    /// The torch runs on five bars of charge, and each one is worth roughly a minute — but only if
    /// you keep switching it off.
    ///
    /// Leaving it burning heats the cell: the longer it stays continuously on, the faster it drains,
    /// up to about a quarter again. A bar you sip at lasts its full minute; a bar you hold down for
    /// five straight minutes gives you closer to forty-five seconds. That is the entire mechanic —
    /// the light is not rationed by a number you watch, it is rationed by the habit of turning it off,
    /// which is also exactly the habit that keeps you alive around the Manager.
    ///
    /// Owner-only. Running dry forces the light off and it cannot be switched back on until a battery
    /// is found, so the last bar is a genuine decision rather than a warning.
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkState))]
    public class FlashlightBattery : MonoBehaviour
    {
        public const int Bars = 5;

        [SerializeField] private PlayerNetworkState state;

        [Tooltip("Seconds one bar lasts when the torch is used in short bursts.")]
        [SerializeField] private float secondsPerBar = 60f;
        [Tooltip("Drain multiplier once it has been on continuously for a long stretch. 1.3 turns a " +
            "60s bar into roughly 46s — the 10-15s penalty per bar for never switching off.")]
        [SerializeField] private float continuousPenalty = 1.3f;
        [Tooltip("Seconds of unbroken use to reach that full penalty.")]
        [SerializeField] private float penaltyRampSeconds = 90f;
        [Tooltip("Seconds off before the cell is considered rested again.")]
        [SerializeField] private float coolSeconds = 12f;
        [Tooltip("Bars restored by one battery pickup.")]
        [SerializeField] private float barsPerBattery = 2f;

        public static FlashlightBattery Local { get; private set; }

        /// <summary>Remaining charge in bars, 0..5. Fractional — the UI draws partial bars.</summary>
        public float Charge { get; private set; } = Bars;
        public bool IsDead => Charge <= 0f;

        private float continuousOn;

        private void Awake()
        {
            if (state == null) state = GetComponent<PlayerNetworkState>();
        }

        private void Update()
        {
            if (state == null || !state.IsLocalPlayer) return;
            Local = this;

            if (!state.FlashlightOn)
            {
                // Resting. The penalty decays rather than resetting instantly, so flicking it off for
                // half a second does not launder a long burn.
                continuousOn = Mathf.MoveTowards(continuousOn, 0f,
                    Time.deltaTime * (penaltyRampSeconds / Mathf.Max(0.1f, coolSeconds)));
                return;
            }

            continuousOn += Time.deltaTime;
            float penalty = Mathf.Lerp(1f, continuousPenalty,
                Mathf.Clamp01(continuousOn / Mathf.Max(0.1f, penaltyRampSeconds)));

            Charge -= (Time.deltaTime / Mathf.Max(0.1f, secondsPerBar)) * penalty;

            if (Charge <= 0f)
            {
                Charge = 0f;
                // Dies rather than dimming. A torch that fades out gently would let you keep using it
                // past the point where it should have cost you something.
                state.ToggleFlashlight();
                var click = GameSfx.SwitchFlip;
                if (click != null) AudioSource.PlayClipAtPoint(click, transform.position, 0.7f);
            }
        }

        /// <summary>Spends a battery pickup. Returns false if already full, so the item is not wasted.</summary>
        public bool AddBattery()
        {
            if (Charge >= Bars - 0.01f) return false;
            Charge = Mathf.Min(Bars, Charge + barsPerBattery);
            continuousOn = 0f;
            return true;
        }

        /// <summary>Blocks the toggle when flat — checked by FlashlightController.</summary>
        public bool CanTurnOn => Charge > 0f;
    }
}
