using LastWard.Audio;
using LastWard.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastWard.Player
{
    /// <summary>
    /// Hold <b>V</b> to stop breathing.
    ///
    /// This is the counterplay to a thing that hunts by sound. While held you make no breath noise at
    /// all and the Entity's sustained-movement pressure stops accruing — but you are on a clock, and
    /// the clock is shorter the harder you have been working. Sprint to a hiding place and you have
    /// barely ten seconds; walk there calmly and you have fifteen. That is the whole decision: run and
    /// arrive winded, or move slowly and arrive able to wait it out.
    ///
    /// Running out is worse than never holding: the forced exhale is loud, and it happens at the exact
    /// moment you were trying hardest to be silent. Recovery is deliberately slower than the drain, so
    /// panic-holding repeatedly leaves you with nothing left when it matters.
    ///
    /// Owner-only. The replicated flag on <see cref="PlayerNetworkState"/> is what the server-side
    /// senses read, so a client cannot claim to be silent without actually holding.
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkState))]
    public class PlayerBreathHold : MonoBehaviour
    {
        [SerializeField] private FirstPersonMotor motor;
        [SerializeField] private PlayerNetworkState state;

        [Header("Capacity")]
        [Tooltip("Seconds you can hold after moving calmly.")]
        [SerializeField] private float restedSeconds = 15f;
        [Tooltip("Seconds you can hold if you arrived sprinting. You are already out of breath.")]
        [SerializeField] private float windedSeconds = 10f;
        [Tooltip("How long after sprinting you are still counted as winded.")]
        [SerializeField] private float windedMemory = 4f;
        [Tooltip("Seconds of normal breathing to recover a full lungful. Slower than the drain, so " +
            "repeated panic-holds genuinely cost you.")]
        [SerializeField] private float recoverySeconds = 12f;

        [Header("Failure")]
        [Tooltip("Noise radius of the gasp when you run out. Loud, and at the worst possible moment.")]
        [SerializeField] private float gaspNoiseRadius = 12f;
        [Tooltip("Seconds you cannot hold again after failing — you are gulping air.")]
        [SerializeField] private float gaspLockout = 3.5f;

        /// <summary>0..1 of a full lungful. Drains while held, refills while breathing.</summary>
        public float Breath { get; private set; } = 1f;
        public bool IsHolding { get; private set; }
        /// <summary>Seconds of hold left right now, for the HUD.</summary>
        public float SecondsLeft => Breath * CurrentCapacity;

        public static PlayerBreathHold Local { get; private set; }

        private float lastSprintTime = -99f;
        private float lockoutUntil;

        private float CurrentCapacity =>
            Time.time - lastSprintTime <= windedMemory ? windedSeconds : restedSeconds;

        private void Awake()
        {
            if (state == null) state = GetComponent<PlayerNetworkState>();
            if (motor == null) motor = GetComponent<FirstPersonMotor>();
        }

        private void Update()
        {
            if (state == null || !state.IsLocalPlayer) return;
            Local = this;

            // The dead do not hold their breath.
            if (!state.IsAlive)
            {
                if (IsHolding) SetHolding(false);
                return;
            }

            if (motor != null && motor.IsSprinting && motor.IsMoving) lastSprintTime = Time.time;

            bool wants = Keyboard.current != null && Keyboard.current.vKey.isPressed;
            bool canHold = Time.time >= lockoutUntil && Breath > 0f;

            if (wants && canHold)
            {
                if (!IsHolding) SetHolding(true);
                // Capacity is read live, so starting a hold rested and then being scared does not
                // retroactively shorten it — only how hard you were working when you began.
                Breath -= Time.deltaTime / Mathf.Max(0.1f, CurrentCapacity);
                if (Breath <= 0f)
                {
                    Breath = 0f;
                    Gasp();
                }
            }
            else
            {
                if (IsHolding) SetHolding(false);
                Breath = Mathf.MoveTowards(Breath, 1f, Time.deltaTime / Mathf.Max(0.1f, recoverySeconds));
            }
        }

        private void SetHolding(bool value)
        {
            IsHolding = value;
            state.SetHoldingBreath(value);
        }

        /// <summary>You could not keep it in. Loud, and exactly when you needed silence.</summary>
        private void Gasp()
        {
            SetHolding(false);
            lockoutUntil = Time.time + gaspLockout;

            var clip = GameSfx.BreathingHeavy;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 1f);
            LastWard.Core.GameEvents.RaiseNoiseEmitted(transform.position, gaspNoiseRadius,
                LastWard.Core.NoiseSource.Sprint);
        }
    }
}
